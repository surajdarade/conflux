namespace Conflux.Inventory.Features.Inventory.CreateInventory;

/// <summary>
/// Represents the response returned after successfully creating an inventory item.
/// </summary>
public sealed record CreateInventoryResponse
{
    /// <summary>
    /// Gets the unique identifier of the inventory item.
    /// </summary>
    public required Guid InventoryId { get; init; }

    /// <summary>
    /// Gets the stock keeping unit associated with the inventory item.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the quantity currently available for reservation.
    /// </summary>
    public int AvailableQuantity { get; init; }

    /// <summary>
    /// Gets the quantity currently reserved.
    /// </summary>
    public int ReservedQuantity { get; init; }
}