namespace Conflux.Contracts.Events;

/// <summary>Represents an order that has completed payment capture.</summary>
public sealed record OrderConfirmed
{
    /// <summary>Gets the event identifier.</summary>
    public required Guid EventId { get; init; }

    /// <summary>Gets the event occurrence time.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Gets the business correlation identifier.</summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>Gets the causation event identifier, when available.</summary>
    public Guid? CausationId { get; init; }

    /// <summary>Gets the order identifier.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>Gets the customer identifier.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>Gets the captured payment identifier.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>Gets the order total.</summary>
    public required decimal TotalAmount { get; init; }

    /// <summary>Gets the order currency.</summary>
    public required string Currency { get; init; }
}
