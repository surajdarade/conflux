namespace Conflux.Inventory.Features.Inventory.GetInventory;

/// <summary>
/// Represents the response returned when retrieving an inventory item.
/// </summary>
public sealed record GetInventoryResponse
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

    /// <summary>
    /// Gets the UTC timestamp at which the inventory item was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the UTC timestamp at which the inventory item was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}