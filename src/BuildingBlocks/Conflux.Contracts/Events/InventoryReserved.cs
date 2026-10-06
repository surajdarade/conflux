namespace Conflux.Contracts.Events;

/// <summary>
/// Represents an event raised when inventory is successfully reserved.
/// </summary>
public sealed record InventoryReserved {
    /// <summary>
    /// Gets the unique identifier of this event.
    /// </summary>
    public required Guid EventId { get; init; }

    /// <summary>
    /// Gets the time at which the event occurred.
    /// </summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Gets the correlation identifier for the business operation.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Gets the identifier of the event that caused this event, when available.
    /// </summary>
    public Guid? CausationId { get; init; }

    /// <summary>
    /// Gets the inventory reservation identifier.
    /// </summary>
    public required Guid ReservationId { get; init; }

    /// <summary>
    /// Gets the SKU whose inventory was reserved.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the quantity reserved.
    /// </summary>
    public required int Quantity { get; init; }
}