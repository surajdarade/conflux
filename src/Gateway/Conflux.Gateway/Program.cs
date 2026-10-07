using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Conflux.Observability;
using Conflux.Gateway.RateLimiting;
using StackExchange.Redis;
var builder = WebApplication.CreateBuilder(args);
builder.AddConfluxObservability("conflux-gateway");

static void ConfigureServiceDiscoveryDestination(
    IConfiguration configuration,
    string serviceName,
    string clusterName)
{
    var address =
        configuration[$"services:{serviceName}:http:0"];

    if (!string.IsNullOrWhiteSpace(address))
    {
        configuration[
            $"ReverseProxy:Clusters:{clusterName}:Destinations:{serviceName}-service:Address"] =
            address;
    }
}

ConfigureServiceDiscoveryDestination(
    builder.Configuration,
    "identity",
    "identity-cluster");
ConfigureServiceDiscoveryDestination(
    builder.Configuration,
    "catalog",
    "catalog-cluster");
ConfigureServiceDiscoveryDestination(
    builder.Configuration,
    "inventory",
    "inventory-cluster");
ConfigureServiceDiscoveryDestination(
    builder.Configuration,
    "order",
    "order-cluster");
ConfigureServiceDiscoveryDestination(
    builder.Configuration,
    "payment",
    "payment-cluster");
ConfigureServiceDiscoveryDestination(
    builder.Configuration,
    "fulfillment",
    "fulfillment-cluster");

builder.Services.AddHealthChecks();

var redisEnabled = builder.Configuration.GetValue<bool>("Redis:Enabled");
if (redisEnabled)
{
    var redisConnection = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Redis connection string is required when Redis is enabled.");

    builder.Services.AddSingleton<IConnectionMultiplexer>(
        ConnectionMultiplexer.Connect(redisConnection));

    builder.Services.AddSingleton(new RedisRateLimitOptions
    {
        RequestsPerWindow = builder.Configuration.GetValue<long>(
            "Redis:RateLimit:RequestsPerWindow", 100),
        WindowSeconds = builder.Configuration.GetValue<int>(
            "Redis:RateLimit:WindowSeconds", 1)
    });
}

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(
        builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapHealthChecks(
    "/alive",
    new HealthCheckOptions
    {
        Predicate = static _ => false
    });

app.MapHealthChecks("/ready");
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();

if (redisEnabled)
{
    app.UseMiddleware<RedisRateLimitMiddleware>();
}

app.MapReverseProxy();

app.Run();
