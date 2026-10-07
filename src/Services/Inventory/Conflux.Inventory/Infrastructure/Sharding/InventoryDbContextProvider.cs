using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Conflux.Inventory.Infrastructure.Sharding;

/// <summary>
/// Creates inventory database contexts against the configured physical shards.
/// </summary>
public sealed class InventoryDbContextProvider {
    private readonly Dictionary<
        string,
        IDbContextFactory<InventoryDbContext>> _factories;

    private readonly InventoryShardingOptions _options;

    private readonly InventoryShardRouter? _router;

    /// <summary>
    /// Initializes a new provider using application configuration.
    /// </summary>
    /// <param name="configuration">
    /// The application configuration containing inventory database
    /// and physical-sharding settings.
    /// </param>
    public InventoryDbContextProvider(
        IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        _options =
            new InventoryShardingOptions();

        configuration
            .GetSection(
                InventoryShardingOptions.SectionName)
            .Bind(_options);

        if (!_options.Enabled) {
            var connectionString =
                configuration.GetConnectionString(
                    "InventoryDatabase")
                ?? throw new InvalidOperationException(
                    "InventoryDatabase connection string is required.");

            _factories =
                new(StringComparer.Ordinal)
                {
                    ["default"] =
                        CreateFactory(connectionString)
                };

            return;
        }

        if (_options.Shards.Count == 0) {
            throw new InvalidOperationException(
                "InventorySharding:Shards must contain at least one physical shard.");
        }

        if (_options.Shards.Any(
                shard =>
                    string.IsNullOrWhiteSpace(
                        shard.Name) ||
                    string.IsNullOrWhiteSpace(
                        shard.ConnectionString))) {
            throw new InvalidOperationException(
                "Every physical inventory shard requires a name and connection string.");
        }

        _factories =
            _options.Shards.ToDictionary(
                shard => shard.Name,
                shard =>
                    CreateFactory(
                        shard.ConnectionString),
                StringComparer.Ordinal);

        _router =
            new InventoryShardRouter(
                _options);
    }

    /// <summary>
    /// Initializes a provider using an existing database context.
    /// This constructor is intended for integration-test compatibility
    /// and is intentionally internal so ASP.NET Core dependency injection
    /// does not consider it during service activation.
    /// </summary>
    /// <param name="dbContext">
    /// The existing inventory database context.
    /// </param>
    internal InventoryDbContextProvider(
        InventoryDbContext dbContext) {
        ArgumentNullException.ThrowIfNull(
            dbContext);

        var connectionString =
            dbContext.Database
                .GetDbConnection()
                .ConnectionString;

        if (string.IsNullOrWhiteSpace(
                connectionString)) {
            throw new InvalidOperationException(
                "The supplied InventoryDbContext does not contain a database connection string.");
        }

        _options =
            new InventoryShardingOptions
            {
                Enabled = false
            };

        _factories =
            new(StringComparer.Ordinal)
            {
                ["default"] =
                    CreateFactory(
                        connectionString)
            };
    }

    /// <summary>
    /// Gets whether physical database sharding is enabled.
    /// </summary>
    public bool IsShardingEnabled =>
        _options.Enabled;

    /// <summary>
    /// Gets all configured physical shard names.
    /// </summary>
    public IReadOnlyCollection<string> ShardNames =>
        _factories.Keys;

    /// <summary>
    /// Creates a database context routed by SKU.
    /// </summary>
    /// <param name="sku">
    /// The stock keeping unit used to determine the physical shard.
    /// </param>
    /// <returns>
    /// A database context connected to the appropriate inventory shard.
    /// </returns>
    public InventoryDbContext CreateForSku(
        string sku) {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sku);

        var shard =
            IsShardingEnabled
                ? _router!.GetShardForSku(sku)
                : "default";

        return _factories[shard]
            .CreateDbContext();
    }

    /// <summary>
    /// Creates a database context routed by inventory identifier.
    /// </summary>
    /// <param name="inventoryItemId">
    /// The inventory item identifier used to determine the physical shard.
    /// </param>
    /// <returns>
    /// A database context connected to the appropriate inventory shard.
    /// </returns>
    public InventoryDbContext CreateForInventoryId(
        Guid inventoryItemId) {
        if (inventoryItemId == Guid.Empty) {
            throw new ArgumentException(
                "Inventory item ID cannot be empty.",
                nameof(inventoryItemId));
        }

        var shard =
            IsShardingEnabled
                ? _router!.GetShardForInventoryId(
                    inventoryItemId)
                : "default";

        return _factories[shard]
            .CreateDbContext();
    }

    /// <summary>
    /// Creates a database context for a named physical shard.
    /// </summary>
    /// <param name="shardName">
    /// The configured physical shard name.
    /// </param>
    /// <returns>
    /// A database context connected to the requested physical shard.
    /// </returns>
    public InventoryDbContext CreateForShard(
        string shardName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            shardName);

        if (!_factories.TryGetValue(
                shardName,
                out var factory)) {
            throw new KeyNotFoundException(
                $"Inventory shard '{shardName}' is not configured.");
        }

        return factory.CreateDbContext();
    }

    private static IDbContextFactory<InventoryDbContext>
        CreateFactory(
            string connectionString) {
        if (string.IsNullOrWhiteSpace(
                connectionString)) {
            throw new ArgumentException(
                "A database connection string is required.",
                nameof(connectionString));
        }

        var options =
            new DbContextOptionsBuilder<
                InventoryDbContext>()
                .UseNpgsql(
                    connectionString,
                    npgsql =>
                        npgsql.EnableRetryOnFailure(
                            5))
                .Options;

        return new PooledDbContextFactory<
            InventoryDbContext>(
                options,
                poolSize: 128);
    }
}