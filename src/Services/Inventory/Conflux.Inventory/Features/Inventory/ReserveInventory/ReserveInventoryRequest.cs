namespace Conflux.Inventory.Features.Inventory.ReserveInventory;

/// <summary>
/// Represents the request payload for reserving inventory.
/// </summary>
public sealed record ReserveInventoryRequest
{
    /// <summary>
    /// Gets the quantity that should be reserved.
    /// </summary>
    public int Quantity { get; init; }
}