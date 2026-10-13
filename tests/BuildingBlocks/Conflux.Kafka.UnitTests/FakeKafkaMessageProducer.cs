using Confluent.Kafka;

namespace Conflux.Kafka.UnitTests;

/// <summary>
/// Captures published Kafka messages and supports controlled failures in tests.
/// </summary>
internal sealed class FakeKafkaMessageProducer : IKafkaMessageProducer
{
    /// <summary>Gets the messages captured by this producer.</summary>
    public List<(string Topic, Message<string, string> Message)> PublishedMessages { get; } = [];

    /// <summary>Gets an optional exception to throw during publication.</summary>
    public Exception? PublishException { get; set; }

    /// <summary>Gets whether this producer has been disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public Task ProduceAsync(
        string topic,
        Message<string, string> message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (PublishException is not null)
        {
            return Task.FromException(PublishException);
        }

        PublishedMessages.Add((topic, message));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => IsDisposed = true;
}
