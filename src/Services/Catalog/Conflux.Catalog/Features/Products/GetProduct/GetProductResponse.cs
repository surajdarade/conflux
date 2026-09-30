namespace Conflux.Catalog.Features.Products.GetProduct;

/// <summary>
/// Represents the response returned when retrieving a product.
/// </summary>
public sealed record GetProductResponse
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
    /// Gets the product description.
    /// </summary>
    public required string Description { get; init; }

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

    /// <summary>
    /// Gets the UTC timestamp at which the product was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the UTC timestamp at which the product was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}