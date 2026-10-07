using StackExchange.Redis;

namespace Conflux.Gateway.RateLimiting;

/// <summary>
/// Implements a fixed-window distributed request limit using Redis.
/// </summary>
public sealed class RedisRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDatabase _database;
    private readonly RedisRateLimitOptions _options;
    private readonly ILogger<RedisRateLimitMiddleware> _logger;

    /// <summary>Initializes the middleware.</summary>
    public RedisRateLimitMiddleware(
        RequestDelegate next,
        IConnectionMultiplexer redis,
        RedisRateLimitOptions options,
        ILogger<RedisRateLimitMiddleware> logger)
    {
        _next = next;
        _database = redis.GetDatabase();
        _options = options;
        _logger = logger;
    }

    /// <summary>Processes one HTTP request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var identity = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var window = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / _options.WindowSeconds;
        var key = $"conflux:rate:{identity}:{window}";

        try
        {
            var count = await _database.StringIncrementAsync(key);
            if (count == 1)
            {
                await _database.KeyExpireAsync(key, TimeSpan.FromSeconds(_options.WindowSeconds + 1));
            }

            context.Response.Headers["X-RateLimit-Limit"] = _options.RequestsPerWindow.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, _options.RequestsPerWindow - count).ToString();

            if (count > _options.RequestsPerWindow)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }
        }
        catch (RedisException exception)
        {
            _logger.LogWarning(exception, "Redis rate limiting failed open for {Path}.", context.Request.Path);
        }

        await _next(context);
    }
}

/// <summary>Configuration for the gateway Redis rate limiter.</summary>
public sealed class RedisRateLimitOptions
{
    /// <summary>Gets or sets the number of requests allowed per window.</summary>
    public long RequestsPerWindow { get; set; } = 100;

    /// <summary>Gets or sets the fixed window duration in seconds.</summary>
    public int WindowSeconds { get; set; } = 1;
}
