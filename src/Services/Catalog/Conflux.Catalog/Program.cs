using Conflux.Catalog.Features.Products.CreateProduct;
using Conflux.Catalog.Features.Products.GetProduct;
using Conflux.Catalog.Features.Products.ListProducts;
using Conflux.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("CatalogDatabase")));

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapCreateProductEndpoint();
app.MapGetProductEndpoint();
app.MapListProductsEndpoint();

app.Run();

/// <summary>
/// Provides an accessible entry point type for ASP.NET Core integration tests.
/// </summary>
public partial class Program
{
}