namespace Conflux.Inventory.Features.Inventory.ReserveInventory;

/// <summary>
/// Represents the response returned after successfully reserving inventory.
/// </summary>
public sealed record ReserveInventoryResponse
{
    /// <summary>
    /// Gets the unique identifier of the inventory item.
    /// </summary>
    public required Guid InventoryId { get; init; }

    /// <summary>
    /// Gets the stock keeping unit associated with the reservation.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the quantity reserved by this operation.
    /// </summary>
    public int ReservedQuantity { get; init; }

    /// <summary>
    /// Gets the quantity remaining available for reservation.
    /// </summary>
    public int AvailableQuantity { get; init; }

    /// <summary>
    /// Gets the total quantity currently reserved for the inventory item.
    /// </summary>
    public int TotalReservedQuantity { get; init; }
}