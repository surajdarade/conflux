namespace Conflux.Inventory.Features.Inventory.ReleaseInventory;

/// <summary>
/// Represents the response returned after releasing an inventory reservation.
/// </summary>
public sealed record ReleaseInventoryResponse {
    /// <summary>
    /// Gets the unique identifier of the inventory item.
    /// </summary>
    public required Guid InventoryId { get; init; }

    /// <summary>
    /// Gets the stock keeping unit associated with the reservation.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the quantity returned to available inventory.
    /// </summary>
    public int ReleasedQuantity { get; init; }

    /// <summary>
    /// Gets the quantity currently available for reservation.
    /// </summary>
    public int AvailableQuantity { get; init; }

    /// <summary>
    /// Gets the quantity currently reserved.
    /// </summary>
    public int TotalReservedQuantity { get; init; }

    /// <summary>
    /// Gets a value indicating whether the reservation was already released.
    /// </summary>
    public bool AlreadyReleased { get; init; }
}