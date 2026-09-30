namespace Conflux.Catalog.Features.Products.CreateProduct;

/// <summary>
/// Represents the request payload for creating a product.
/// </summary>
public sealed record CreateProductRequest
{
    /// <summary>
    /// Gets the stock keeping unit that uniquely identifies the product.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the display name of the product.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the product description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the price of one unit of the product.
    /// </summary>
    public decimal Price { get; init; }

    /// <summary>
    /// Gets the ISO 4217 currency code associated with the product price.
    /// </summary>
    public required string Currency { get; init; }
}