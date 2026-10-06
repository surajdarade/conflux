using Conflux.Outbox;

namespace Conflux.Inventory.OutboxPublisher.Kafka;

/// <summary>
/// Publishes Inventory Outbox messages to Kafka.
/// </summary>
public interface IKafkaEventPublisher
{
    /// <summary>
    /// Publishes an Outbox message to the specified Kafka topic.
    /// </summary>
    /// <param name="message">
    /// The Outbox message to publish.
    /// </param>
    /// <param name="topic">
    /// The Kafka topic.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous publication operation.
    /// </returns>
    Task PublishAsync(
        OutboxMessage message,
        string topic,
        CancellationToken cancellationToken);
}