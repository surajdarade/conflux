namespace Conflux.Catalog.Features.Products.ListProducts;

/// <summary>
/// Represents the response returned when listing products.
/// </summary>
public sealed record ListProductsResponse
{
    /// <summary>
    /// Gets the products returned for the requested page.
    /// </summary>
    public required IReadOnlyList<ProductSummary> Products { get; init; }

    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Gets the maximum number of products requested for the page.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Gets the total number of products matching the query.
    /// </summary>
    public long TotalCount { get; init; }

    /// <summary>
    /// Gets the total number of pages available.
    /// </summary>
    public int TotalPages { get; init; }
}

/// <summary>
/// Represents the summary information for a product in a product listing.
/// </summary>
public sealed record ProductSummary
{
    /// <summary>
    /// Gets the unique identifier of the product.
    /// </summary>
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Gets the stock keeping unit of the product.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the display name of the product.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the price of one unit of the product.
    /// </summary>
    public decimal Price { get; init; }

    /// <summary>
    /// Gets the ISO 4217 currency code associated with the product price.
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// Gets a value indicating whether the product is currently active.
    /// </summary>
    public bool IsActive { get; init; }
}