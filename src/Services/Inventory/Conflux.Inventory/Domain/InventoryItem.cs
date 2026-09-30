namespace Conflux.Inventory.Domain;

/// <summary>
/// Represents the inventory state for a single stock keeping unit.
/// </summary>
public sealed class InventoryItem
{
    private InventoryItem()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InventoryItem"/> class.
    /// </summary>
    /// <param name="id">
    /// The unique identifier of the inventory item.
    /// </param>
    /// <param name="sku">
    /// The stock keeping unit managed by this inventory item.
    /// </param>
    /// <param name="availableQuantity">
    /// The quantity currently available for reservation.
    /// </param>
    public InventoryItem(
        Guid id,
        string sku,
        int availableQuantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        if (availableQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableQuantity),
                "Available quantity cannot be negative.");
        }

        Id = id;
        Sku = sku.Trim().ToUpperInvariant();
        AvailableQuantity = availableQuantity;
        ReservedQuantity = 0;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Gets the unique identifier of the inventory item.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the stock keeping unit managed by this inventory item.
    /// </summary>
    public string Sku { get; private set; } = null!;

    /// <summary>
    /// Gets the quantity currently available for reservation.
    /// </summary>
    public int AvailableQuantity { get; private set; }

    /// <summary>
    /// Gets the quantity currently reserved.
    /// </summary>
    public int ReservedQuantity { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which the inventory item was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which the inventory item was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Reserves the requested quantity from the available inventory.
    /// </summary>
    /// <param name="quantity">
    /// The quantity to reserve.
    /// </param>
    public void Reserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Reservation quantity must be greater than zero.");
        }

        if (quantity > AvailableQuantity)
        {
            throw new InvalidOperationException(
                "Insufficient inventory.");
        }

        AvailableQuantity -= quantity;
        ReservedQuantity += quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}