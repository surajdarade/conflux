namespace Conflux.Order.Domain;

/// <summary>
/// Represents the lifecycle state of an order.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// The order has been created but processing has not completed.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Inventory has been successfully reserved for the order.
    /// </summary>
    InventoryReserved = 1,

    /// <summary>
    /// Payment processing is currently pending.
    /// </summary>
    PaymentPending = 2,

    /// <summary>
    /// The order has been successfully confirmed.
    /// </summary>
    Confirmed = 3,

    /// <summary>
    /// The order has been cancelled.
    /// </summary>
    Cancelled = 4,

    /// <summary>
    /// The order processing workflow has failed.
    /// </summary>
    Failed = 5
}