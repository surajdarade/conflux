namespace Conflux.Catalog.ReadModel;

/// <summary>
/// Denormalized product projection optimized for catalog reads.
/// </summary>
public sealed class ProductReadModel
{
    private ProductReadModel()
    {
    }

    /// <summary>Initializes a new product projection.</summary>
    public ProductReadModel(
        Guid productId,
        string sku,
        string name,
        string description,
        decimal price,
        string currency,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        ProductId = productId;
        Sku = sku;
        Name = name;
        Description = description;
        Price = price;
        Currency = currency;
        IsActive = isActive;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Gets the product identifier.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Gets the SKU.</summary>
    public string Sku { get; private set; } = null!;

    /// <summary>Gets the product name.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Gets the product description.</summary>
    public string Description { get; private set; } = null!;

    /// <summary>Gets the product price.</summary>
    public decimal Price { get; private set; }

    /// <summary>Gets the currency.</summary>
    public string Currency { get; private set; } = null!;

    /// <summary>Gets whether the product is active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the original source creation time.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets the last source update time.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Updates the projection from a source product.</summary>
    public void Apply(
        string name,
        string description,
        decimal price,
        string currency,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Name = name;
        Description = description;
        Price = price;
        Currency = currency;
        IsActive = isActive;
        UpdatedAt = updatedAt;
    }
}
