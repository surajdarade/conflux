namespace Conflux.Outbox;

/// <summary>
/// Represents a durable integration event waiting to be published.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="OutboxMessage"/> class.
    /// </summary>
    /// <param name="id">
    /// The unique identifier of the Outbox message.
    /// </param>
    /// <param name="occurredAt">
    /// The UTC timestamp when the event occurred.
    /// </param>
    /// <param name="eventType">
    /// The integration event type.
    /// </param>
    /// <param name="payload">
    /// The serialized integration event payload.
    /// </param>
    /// <param name="correlationId">
    /// The correlation identifier for the business operation.
    /// </param>
    /// <param name="causationId">
    /// The identifier of the event that caused this event.
    /// </param>
    public OutboxMessage(
        Guid id,
        DateTimeOffset occurredAt,
        string eventType,
        string payload,
        Guid correlationId,
        Guid? causationId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Outbox message identifier is required.",
                nameof(id));
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException(
                "Event type is required.",
                nameof(eventType));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException(
                "Payload is required.",
                nameof(payload));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Correlation identifier is required.",
                nameof(correlationId));
        }

        Id = id;
        OccurredAt = occurredAt;
        EventType = eventType.Trim();
        Payload = payload;
        CorrelationId = correlationId;
        CausationId = causationId;
    }

    /// <summary>
    /// Gets the unique identifier of the Outbox message.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the event occurred.
    /// </summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>
    /// Gets the integration event type.
    /// </summary>
    public string EventType { get; private set; } = null!;

    /// <summary>
    /// Gets the serialized integration event payload.
    /// </summary>
    public string Payload { get; private set; } = null!;

    /// <summary>
    /// Gets the correlation identifier for the business operation.
    /// </summary>
    public Guid CorrelationId { get; private set; }

    /// <summary>
    /// Gets the identifier of the event that caused this event.
    /// </summary>
    public Guid? CausationId { get; private set; }

    /// <summary>
    /// Gets the timestamp of the first publication attempt.
    /// </summary>
    public DateTimeOffset? FirstAttemptedAt { get; private set; }

    /// <summary>
    /// Gets the timestamp when the message was successfully published.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>
    /// Gets the number of publication attempts.
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// Gets the most recent publication error.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Gets the timestamp when the current publication lease was acquired.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; private set; }

    /// <summary>
    /// Gets the identifier of the publisher that currently owns the lease.
    /// </summary>
    public Guid? ClaimedBy { get; private set; }

    /// <summary>
    /// Records a publication attempt.
    /// </summary>
    /// <param name="attemptedAt">
    /// The UTC timestamp of the attempt.
    /// </param>
    public void MarkAttempted(
        DateTimeOffset attemptedAt)
    {
        FirstAttemptedAt ??= attemptedAt;
        AttemptCount++;
    }

    /// <summary>
    /// Claims the Outbox message for a publisher.
    /// </summary>
    /// <param name="claimedAt">
    /// The UTC timestamp when the lease was acquired.
    /// </param>
    /// <param name="publisherId">
    /// The identifier of the publisher acquiring the lease.
    /// </param>
    public void Claim(
        DateTimeOffset claimedAt,
        Guid publisherId)
    {
        if (publisherId == Guid.Empty)
        {
            throw new ArgumentException(
                "Publisher identifier is required.",
                nameof(publisherId));
        }

        ClaimedAt = claimedAt;
        ClaimedBy = publisherId;
    }

    /// <summary>
    /// Releases the current publication lease.
    /// </summary>
    public void ReleaseClaim()
    {
        ClaimedAt = null;
        ClaimedBy = null;
    }

    /// <summary>
    /// Records a publication failure.
    /// </summary>
    /// <param name="error">
    /// The publication error.
    /// </param>
    public void MarkFailed(
        string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException(
                "Publication error is required.",
                nameof(error));
        }

        LastError = error;
    }

    /// <summary>
    /// Marks the message as successfully published.
    /// </summary>
    /// <param name="publishedAt">
    /// The UTC timestamp when publication succeeded.
    /// </param>
    public void MarkPublished(
        DateTimeOffset publishedAt)
    {
        PublishedAt = publishedAt;
        LastError = null;
        ReleaseClaim();
    }
}