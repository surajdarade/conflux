using Conflux.Catalog.Infrastructure;
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
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == productId,
                cancellationToken);

        if (product is null)
        {
            return Results.NotFound(
                new
                {
                    error = "Product was not found."
                });
        }

        var response = new GetProductResponse
        {
            ProductId = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            Currency = product.Currency,
            IsActive = product.IsActive,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt
        };

        return Results.Ok(response);
    }
}