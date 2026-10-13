using Confluent.Kafka;

namespace Conflux.Kafka;

/// <summary>
/// Defines the consumer operations required by Kafka background workers.
/// </summary>
public interface IKafkaMessageConsumer : IDisposable
{
    /// <summary>Subscribes to the specified topics.</summary>
    void Subscribe(IEnumerable<string> topics);

    /// <summary>Consumes the next message or throws when consumption fails.</summary>
    ConsumeResult<string, string> Consume(CancellationToken cancellationToken);

    /// <summary>Commits the offset of a consumed message.</summary>
    void Commit(ConsumeResult<string, string> result);

    /// <summary>Closes the consumer and releases its group membership.</summary>
    void Close();
}
