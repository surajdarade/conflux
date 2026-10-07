using Conflux.Catalog.Infrastructure;
using Conflux.Catalog.ReadModel;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Catalog.Features.Products.GetProduct;

/// <summary>
/// Provides the HTTP endpoint for retrieving a product.
/// </summary>
public static class GetProductEndpoint
{
    /// <summary>
    /// Maps the product retrieval endpoint to the application.
    /// </summary>
    /// <param name="endpoints">
    /// The endpoint route builder used to register the HTTP endpoint.
    /// </param>
    public static void MapGetProductEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/catalog/products/{productId:guid}",
                HandleAsync)
            .WithName("GetProduct")
            .WithTags("Catalog");
    }

    private static async Task<IResult> HandleAsync(
        Guid productId,
        CatalogDbContext dbContext,
        ProductReadCache cache,
        CancellationToken cancellationToken)
    {
        var cachedProduct = await cache.GetOrCreateAsync(
            productId,
            async token => await dbContext.ProductReadModels
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.ProductId == productId, token),
            cancellationToken);

        if (cachedProduct is null)
        {
            return Results.NotFound(new { error = "Product was not found." });
        }

        var response = new GetProductResponse
        {
            ProductId = cachedProduct.ProductId,
            Sku = cachedProduct.Sku,
            Name = cachedProduct.Name,
            Description = cachedProduct.Description,
            Price = cachedProduct.Price,
            Currency = cachedProduct.Currency,
            IsActive = cachedProduct.IsActive,
            CreatedAt = cachedProduct.CreatedAt,
            UpdatedAt = cachedProduct.UpdatedAt
        };

        return Results.Ok(response);
    }
}