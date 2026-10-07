namespace Conflux.Contracts.Events;

/// <summary>
/// Represents the Order service event emitted after all order inventory
/// reservations have been durably recorded as successful.
/// </summary>
public sealed record OrderInventoryReserved
{
    /// <summary>Gets the unique identifier of the event.</summary>
    public required Guid EventId { get; init; }

    /// <summary>Gets the UTC time at which the event occurred.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Gets the business correlation identifier.</summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>Gets the event that caused this event, when available.</summary>
    public Guid? CausationId { get; init; }

    /// <summary>Gets the order identifier.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>Gets the customer identifier.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>Gets the total order amount.</summary>
    public required decimal TotalAmount { get; init; }

    /// <summary>Gets the order currency.</summary>
    public required string Currency { get; init; }
}
