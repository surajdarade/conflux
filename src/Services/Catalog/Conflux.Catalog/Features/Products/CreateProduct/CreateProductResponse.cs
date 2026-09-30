namespace Conflux.Catalog.Features.Products.CreateProduct;

/// <summary>
/// Represents the response returned after successfully creating a product.
/// </summary>
public sealed record CreateProductResponse
{
    /// <summary>
    /// Gets the unique identifier of the created product.
    /// </summary>
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Gets the stock keeping unit of the created product.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the display name of the created product.
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