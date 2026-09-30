using Conflux.Catalog.Domain;
using Conflux.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Catalog.Features.Products.CreateProduct;

/// <summary>
/// Provides the HTTP endpoint for creating products.
/// </summary>
public static class CreateProductEndpoint
{
    /// <summary>
    /// Maps the product creation endpoint to the application.
    /// </summary>
    /// <param name="endpoints">
    /// The endpoint route builder used to register the HTTP endpoint.
    /// </param>
    public static void MapCreateProductEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/catalog/products",
                HandleAsync)
            .WithName("CreateProduct")
            .WithTags("Catalog");
    }

    private static async Task<IResult> HandleAsync(
        CreateProductRequest request,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return Results.BadRequest(
                new
                {
                    error = "SKU is required."
                });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(
                new
                {
                    error = "Product name is required."
                });
        }

        if (request.Price < 0)
        {
            return Results.BadRequest(
                new
                {
                    error = "Product price cannot be negative."
                });
        }

        if (string.IsNullOrWhiteSpace(request.Currency))
        {
            return Results.BadRequest(
                new
                {
                    error = "Currency is required."
                });
        }

        if (request.Currency.Trim().Length != 3)
        {
            return Results.BadRequest(
                new
                {
                    error = "Currency must be a three-letter ISO 4217 code."
                });
        }

        var sku = request.Sku.Trim().ToUpperInvariant();

        var skuExists = await dbContext.Products
            .AnyAsync(
                product => product.Sku == sku,
                cancellationToken);

        if (skuExists)
        {
            return Results.Conflict(
                new
                {
                    error = "A product with this SKU already exists."
                });
        }

        var product = new Product(
            Guid.NewGuid(),
            sku,
            request.Name,
            request.Description,
            request.Price,
            request.Currency);

        dbContext.Products.Add(product);

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new CreateProductResponse
        {
            ProductId = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            Price = product.Price,
            Currency = product.Currency,
            IsActive = product.IsActive
        };

        return Results.Created(
            $"/api/v1/catalog/products/{product.Id}",
            response);
    }
}