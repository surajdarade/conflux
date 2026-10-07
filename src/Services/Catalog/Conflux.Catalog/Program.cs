using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Conflux.Observability;
using Conflux.Catalog.Features.Products.CreateProduct;
using Conflux.Catalog.Features.Products.GetProduct;
using Conflux.Catalog.Features.Products.ListProducts;
using Conflux.Catalog.Infrastructure;
using Conflux.Catalog.ReadModel;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddConfluxObservability("conflux-catalog");

builder.Services.AddHealthChecks();

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("CatalogDatabase")));

builder.Services.AddScoped<ProductProjectionService>();
builder.Services.AddScoped<ProductReadCache>();

if (builder.Configuration.GetValue<bool>("Redis:Enabled"))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = builder.Configuration.GetConnectionString("Redis");
        options.InstanceName = "conflux:catalog:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var migrationDbContext = migrationScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await migrationDbContext.Database.MigrateAsync();
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

app.MapCreateProductEndpoint();
app.MapGetProductEndpoint();
app.MapListProductsEndpoint();

app.MapPost(
        "/api/v1/catalog/admin/read-model/rebuild",
        async (ProductProjectionService projectionService, CancellationToken cancellationToken) =>
        {
            await projectionService.RebuildAsync(cancellationToken);
            return Results.NoContent();
        })
    .WithName("RebuildCatalogReadModel");

app.Run();

/// <summary>
/// Provides an accessible entry point type for ASP.NET Core integration tests.
/// </summary>
public partial class Program
{
}