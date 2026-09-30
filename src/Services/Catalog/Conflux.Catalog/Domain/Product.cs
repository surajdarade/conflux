namespace Conflux.Catalog.Domain;

/// <summary>
/// Represents a product that can be offered for purchase through Conflux.
/// </summary>
public sealed class Product {
    private Product() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Product"/> class.
    /// </summary>
    /// <param name="id">
    /// The unique identifier of the product.
    /// </param>
    /// <param name="sku">
    /// The stock keeping unit that uniquely identifies the sellable item.
    /// </param>
    /// <param name="name">
    /// The display name of the product.
    /// </param>
    /// <param name="description">
    /// The product description.
    /// </param>
    /// <param name="price">
    /// The monetary price of one unit.
    /// </param>
    /// <param name="currency">
    /// The ISO 4217 currency code associated with the price.
    /// </param>
    public Product(
        Guid id,
        string sku,
        string name,
        string description,
        decimal price,
        string currency) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (price < 0) {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                "Product price cannot be negative.");
        }

        Id = id;
        Sku = sku.Trim().ToUpperInvariant();
        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        Price = price;
        Currency = currency.Trim().ToUpperInvariant();
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Gets the unique identifier of the product.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the stock keeping unit of the product.
    /// </summary>
    public string Sku { get; private set; } = null!;

    /// <summary>
    /// Gets the display name of the product.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Gets the product description.
    /// </summary>
    public string Description { get; private set; } = null!;

    /// <summary>
    /// Gets the price of one unit of the product.
    /// </summary>
    public decimal Price { get; private set; }

    /// <summary>
    /// Gets the ISO 4217 currency code for the product price.
    /// </summary>
    public string Currency { get; private set; } = null!;

    /// <summary>
    /// Gets a value indicating whether the product can currently be purchased.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which the product was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which the product was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Updates the mutable product information.
    /// </summary>
    /// <param name="name">
    /// The new product name.
    /// </param>
    /// <param name="description">
    /// The new product description.
    /// </param>
    /// <param name="price">
    /// The new product price.
    /// </param>
    /// <param name="currency">
    /// The ISO 4217 currency code for the new price.
    /// </param>
    public void UpdateDetails(
        string name,
        string description,
        decimal price,
        string currency) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (price < 0) {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                "Product price cannot be negative.");
        }

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        Price = price;
        Currency = currency.Trim().ToUpperInvariant();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Deactivates the product so that it can no longer be offered for purchase.
    /// </summary>
    public void Deactivate() {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Activates the product so that it can be offered for purchase.
    /// </summary>
    public void Activate() {
        IsActive = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}