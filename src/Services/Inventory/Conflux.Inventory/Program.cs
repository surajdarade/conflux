using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Conflux.Observability;
using Conflux.Inventory.Application.Inventory;
using Conflux.Inventory.Features.Inventory.CreateInventory;
using Conflux.Inventory.Features.Inventory.GetInventory;
using Conflux.Inventory.Features.Inventory.ReleaseInventory;
using Conflux.Inventory.Features.Inventory.ReserveInventory;
using Conflux.Inventory.Grpc;
using Conflux.Inventory.Infrastructure;
using Conflux.Inventory.Infrastructure.Sharding;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddConfluxObservability("conflux-inventory");

builder.Services.AddHealthChecks();

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString(
            "InventoryDatabase")));

builder.Services.AddSingleton<InventoryDbContextProvider>();
builder.Services.AddScoped<InventoryApplicationService>(
    serviceProvider =>
        new InventoryApplicationService(
            serviceProvider.GetRequiredService<InventoryDbContextProvider>(),
            serviceProvider.GetRequiredService<ConfluxBusinessMetrics>()));

builder.Services.AddGrpc();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations")) {
    await using var migrationScope = app.Services.CreateAsyncScope();
    var shardProvider = migrationScope.ServiceProvider.GetRequiredService<InventoryDbContextProvider>();

    if (shardProvider.IsShardingEnabled) {
        foreach (var shardName in shardProvider.ShardNames) {
            await using var shardDbContext = shardProvider.CreateForShard(shardName);
            await shardDbContext.Database.MigrateAsync();
        }
    }
    else {
        var migrationDbContext = migrationScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await migrationDbContext.Database.MigrateAsync();
    }
}

app.MapHealthChecks(
    "/alive",
    new HealthCheckOptions
    {
        Predicate = static _ => false
    });

app.MapHealthChecks("/ready");
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();

app.MapCreateInventoryEndpoint();
app.MapGetInventoryEndpoint();
app.MapReserveInventoryEndpoint();
app.MapReleaseInventoryEndpoint();

app.MapGrpcService<InventoryGrpcService>();

app.Run();

/// <summary>
/// Provides an accessible entry point type for ASP.NET Core integration tests.
/// </summary>
public partial class Program {
}