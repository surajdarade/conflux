namespace Conflux.Contracts.Events;

/// <summary>
/// Provides common metadata for integration events.
/// </summary>
/// <typeparam name="TPayload">
/// The event payload type.
/// </typeparam>
public sealed record IntegrationEvent<TPayload>
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
    /// Gets the event payload.
    /// </summary>
    public required TPayload Payload { get; init; }
}