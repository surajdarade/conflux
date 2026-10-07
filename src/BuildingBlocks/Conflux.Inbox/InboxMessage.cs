namespace Conflux.Inbox;

/// <summary>
/// Represents a durable record of an integration event received by a consumer.
/// </summary>
public sealed class InboxMessage
{
    private InboxMessage()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxMessage"/> class.
    /// </summary>
    /// <param name="id">The event identifier.</param>
    /// <param name="consumerName">The logical consumer name.</param>
    /// <param name="eventType">The event type.</param>
    /// <param name="payload">The serialized event payload.</param>
    /// <param name="correlationId">The business correlation identifier.</param>
    /// <param name="causationId">The causing event identifier.</param>
    public InboxMessage(
        Guid id,
        string consumerName,
        string eventType,
        string payload,
        Guid correlationId,
        Guid? causationId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Event identifier is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation identifier is required.", nameof(correlationId));
        }

        Id = id;
        ConsumerName = consumerName.Trim();
        EventType = eventType.Trim();
        Payload = payload;
        CorrelationId = correlationId;
        CausationId = causationId;
        ReceivedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the integration event identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the logical consumer name.</summary>
    public string ConsumerName { get; private set; } = null!;

    /// <summary>Gets the event type.</summary>
    public string EventType { get; private set; } = null!;

    /// <summary>Gets the serialized event payload.</summary>
    public string Payload { get; private set; } = null!;

    /// <summary>Gets the business correlation identifier.</summary>
    public Guid CorrelationId { get; private set; }

    /// <summary>Gets the causing event identifier.</summary>
    public Guid? CausationId { get; private set; }

    /// <summary>Gets the time at which the event was received.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Gets the time at which the event was processed.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Gets the most recent processing error, when any.</summary>
    public string? LastError { get; private set; }

    /// <summary>Marks the message as successfully processed.</summary>
    public void MarkProcessed(DateTimeOffset processedAt)
    {
        ProcessedAt = processedAt;
        LastError = null;
    }

    /// <summary>Records a processing failure while leaving the message retryable.</summary>
    /// <param name="error">The failure description.</param>
    public void MarkFailed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        LastError = error;
    }
}
