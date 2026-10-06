namespace Conflux.Contracts.Events;

/// <summary>
/// Represents an event raised when a payment is successfully captured.
/// </summary>
public sealed record PaymentCaptured
{
    /// <summary>
    /// Gets the unique identifier of the event.
    /// </summary>
    public required Guid EventId { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the event occurred.
    /// </summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Gets the correlation identifier for the business operation.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Gets the identifier of the event that caused this event.
    /// </summary>
    public Guid? CausationId { get; init; }

    /// <summary>
    /// Gets the payment identifier.
    /// </summary>
    public required Guid PaymentId { get; init; }

    /// <summary>
    /// Gets the order identifier associated with the payment.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Gets the customer identifier associated with the payment.
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Gets the captured payment amount.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Gets the payment currency.
    /// </summary>
    public required string Currency { get; init; }
}