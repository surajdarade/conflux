namespace Conflux.Inventory.Features.Inventory.CreateInventory;

/// <summary>
/// Represents the request payload for creating an inventory item.
/// </summary>
public sealed record CreateInventoryRequest
{
    /// <summary>
    /// Gets the stock keeping unit associated with the inventory item.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the initial quantity available for reservation.
    /// </summary>
    public int AvailableQuantity { get; init; }
}