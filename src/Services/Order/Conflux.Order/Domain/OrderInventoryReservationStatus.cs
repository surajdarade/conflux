namespace Conflux.Order.Domain;

/// <summary>
/// Represents the lifecycle state of an inventory reservation associated
/// with an order item.
/// </summary>
public enum OrderInventoryReservationStatus {
    /// <summary>
    /// The reservation has not yet been successfully created in Inventory.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Inventory has been successfully reserved.
    /// </summary>
    Reserved = 1,

    /// <summary>
    /// The inventory reservation has been released.
    /// </summary>
    Released = 2,

    /// <summary>
    /// The reservation operation failed.
    /// </summary>
    Failed = 3
}