using Confluent.Kafka;

namespace Conflux.Kafka;

/// <summary>
/// Adapts a Confluent Kafka consumer to the worker-facing consumer interface.
/// </summary>
internal sealed class ConfluentKafkaMessageConsumer : IKafkaMessageConsumer
{
    private readonly IConsumer<string, string> _consumer;

    /// <summary>Initializes the adapter with an existing Kafka consumer.</summary>
    public ConfluentKafkaMessageConsumer(IConsumer<string, string> consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        _consumer = consumer;
    }

    /// <inheritdoc />
    public void Subscribe(IEnumerable<string> topics) =>
        _consumer.Subscribe(topics);

    /// <inheritdoc />
    public ConsumeResult<string, string> Consume(CancellationToken cancellationToken) =>
        _consumer.Consume(cancellationToken);

    /// <inheritdoc />
    public void Commit(ConsumeResult<string, string> result) =>
        _consumer.Commit(result);

    /// <inheritdoc />
    public void Close() => _consumer.Close();

    /// <inheritdoc />
    public void Dispose() => _consumer.Dispose();
}
