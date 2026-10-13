using Confluent.Kafka;

namespace Conflux.Kafka.UnitTests;

/// <summary>
/// Supplies controlled Kafka messages and records consumer operations in tests.
/// </summary>
internal sealed class FakeKafkaMessageConsumer : IKafkaMessageConsumer
{
    private readonly Queue<ConsumeResult<string, string>> _messages;

    /// <summary>Initializes the fake consumer with messages to consume.</summary>
    public FakeKafkaMessageConsumer(
        IEnumerable<ConsumeResult<string, string>>? messages = null)
    {
        _messages = new Queue<ConsumeResult<string, string>>(messages ?? []);
    }

    /// <summary>Gets the topics subscribed to by the worker.</summary>
    public List<string> SubscribedTopics { get; } = [];

    /// <summary>Gets the offsets committed by the worker.</summary>
    public List<ConsumeResult<string, string>> CommittedMessages { get; } = [];

    /// <summary>Gets whether the consumer was closed.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>Gets whether the consumer was disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public void Subscribe(IEnumerable<string> topics) => SubscribedTopics.AddRange(topics);


    /// <inheritdoc />
    public ConsumeResult<string, string> Consume(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_messages.Count == 0)
        {
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
        }

        return _messages.Dequeue();
    }

    /// <inheritdoc />
    public void Commit(ConsumeResult<string, string> result) =>
        CommittedMessages.Add(result);

    /// <inheritdoc />
    public void Close() => IsClosed = true;

    /// <inheritdoc />
    public void Dispose() => IsDisposed = true;
}
