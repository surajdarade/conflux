using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Conflux.Catalog.ReadModel;

/// <summary>
/// Provides Redis-backed caching for individual catalog products with
/// per-key single-flight protection against cache stampedes.
/// </summary>
public sealed class ProductReadCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> MissLocks = new();
    private readonly IDistributedCache _cache;
    private readonly ILogger<ProductReadCache> _logger;

    /// <summary>Initializes the product cache.</summary>
    /// <param name="cache">The distributed cache implementation.</param>
    /// <param name="logger"></param>
    public ProductReadCache(IDistributedCache cache, ILogger<ProductReadCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Gets a cached product projection.</summary>
    public async Task<ProductReadModel?> GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _cache.GetAsync(Key(productId), cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize<ProductReadModel>(bytes, JsonOptions);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Catalog cache read failed for {ProductId}; falling back to PostgreSQL.", productId);
            return null;
        }
    }

    /// <summary>
    /// Gets a cached product or executes the loader once per key when multiple
    /// requests miss the same cache entry concurrently.
    /// </summary>
    public async Task<ProductReadModel?> GetOrCreateAsync(
        Guid productId,
        Func<CancellationToken, Task<ProductReadModel?>> loader,
        CancellationToken cancellationToken)
    {
        var cached = await GetAsync(productId, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var gate = MissLocks.GetOrAdd(productId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            cached = await GetAsync(productId, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            var loaded = await loader(cancellationToken);
            if (loaded is not null)
            {
                await SetAsync(loaded, cancellationToken);
            }

            return loaded;
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
            {
                MissLocks.TryRemove(new KeyValuePair<Guid, SemaphoreSlim>(productId, gate));
                gate.Dispose();
            }
        }
    }

    /// <summary>Stores a product projection in the cache.</summary>
    public Task SetAsync(ProductReadModel product, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(product, JsonOptions);
        var ttl = TimeSpan.FromMinutes(Random.Shared.NextDouble() * 2 + 4);
        return SetSafeAsync(product.ProductId, bytes, ttl, cancellationToken);
    }

    private async Task SetSafeAsync(Guid productId, byte[] bytes, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetAsync(
                Key(productId),
                bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Catalog cache write failed for {ProductId}; continuing without cache.", productId);
        }
    }

    /// <summary>Removes a cached product projection.</summary>
    public Task RemoveAsync(Guid productId, CancellationToken cancellationToken) =>
        _cache.RemoveAsync(Key(productId), cancellationToken);

    private static string Key(Guid productId) => $"conflux:catalog:product:v1:{productId:N}";
}
