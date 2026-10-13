using Confluent.Kafka;

namespace Conflux.Kafka;

/// <summary>
/// Defines the Kafka publishing operation required by the retry publisher.
/// </summary>
public interface IKafkaMessageProducer : IDisposable
{
    /// <summary>
    /// Publishes a message to the specified topic.
    /// </summary>
    Task ProduceAsync(
        string topic,
        Message<string, string> message,
        CancellationToken cancellationToken);
}
