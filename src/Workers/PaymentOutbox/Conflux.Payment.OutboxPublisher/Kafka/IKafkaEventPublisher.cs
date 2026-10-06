using Conflux.Outbox;

namespace Conflux.Payment.OutboxPublisher.Kafka;

/// <summary>
/// Publishes durable Outbox messages to Kafka.
/// </summary>
public interface IKafkaEventPublisher
{
    /// <summary>
    /// Publishes an Outbox message to Kafka.
    /// </summary>
    /// <param name="message">
    /// The Outbox message to publish.
    /// </param>
    /// <param name="topic">
    /// The Kafka topic to publish to.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    Task PublishAsync(
        OutboxMessage message,
        string topic,
        CancellationToken cancellationToken);
}