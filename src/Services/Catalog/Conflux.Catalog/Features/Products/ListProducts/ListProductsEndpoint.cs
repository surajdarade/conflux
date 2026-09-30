using Conflux.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Catalog.Features.Products.ListProducts;

/// <summary>
/// Provides the HTTP endpoint for listing products.
/// </summary>
public static class ListProductsEndpoint
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;

    /// <summary>
    /// Maps the product listing endpoint to the application.
    /// </summary>
    /// <param name="endpoints">
    /// The endpoint route builder used to register the HTTP endpoint.
    /// </param>
    public static void MapListProductsEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/catalog/products",
                HandleAsync)
            .WithName("ListProducts")
            .WithTags("Catalog");
    }

    private static async Task<IResult> HandleAsync(
        int? page,
        int? pageSize,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var requestedPage = page ?? 1;
        var requestedPageSize = pageSize ?? DefaultPageSize;

        if (requestedPage < 1)
        {
            return Results.BadRequest(
                new
                {
                    error = "Page must be greater than or equal to 1."
                });
        }

        if (requestedPageSize < 1 ||
            requestedPageSize > MaximumPageSize)
        {
            return Results.BadRequest(
                new
                {
                    error =
                        $"Page size must be between 1 and {MaximumPageSize}."
                });
        }

        var totalCount = await dbContext.Products
            .LongCountAsync(cancellationToken);

        var products = await dbContext.Products
            .AsNoTracking()
            .OrderBy(product => product.Id)
            .Skip((requestedPage - 1) * requestedPageSize)
            .Take(requestedPageSize)
            .Select(
                product => new ProductSummary
                {
                    ProductId = product.Id,
                    Sku = product.Sku,
                    Name = product.Name,
                    Price = product.Price,
                    Currency = product.Currency,
                    IsActive = product.IsActive
                })
            .ToListAsync(cancellationToken);

        var totalPages = totalCount == 0
            ? 0
            : checked(
                (int)Math.Ceiling(
                    totalCount / (double)requestedPageSize));

        var response = new ListProductsResponse
        {
            Products = products,
            Page = requestedPage,
            PageSize = requestedPageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };

        return Results.Ok(response);
    }
}