using System.Text;
using Confluent.Kafka;
using Conflux.Outbox;
using Conflux.Kafka;

namespace Conflux.Inventory.OutboxPublisher.Kafka;

/// <summary>
/// Publishes Inventory integration events using Confluent.Kafka.
/// </summary>
public sealed class ConfluentKafkaEventPublisher :
    IKafkaEventPublisher,
    IDisposable
{
    private readonly IProducer<string, string> _producer;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="ConfluentKafkaEventPublisher"/> class.
    /// </summary>
    /// <param name="bootstrapServers">
    /// The Kafka bootstrap server addresses.
    /// </param>
    public ConfluentKafkaEventPublisher(
        string bootstrapServers)
    {
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            throw new ArgumentException(
                "Kafka bootstrap servers are required.",
                nameof(bootstrapServers));
        }

        var configuration = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10000
        };

        _producer =
            new ProducerBuilder<string, string>(
                configuration)
                .Build();
    }

    /// <inheritdoc />
    public async Task PublishAsync(
        OutboxMessage message,
        string topic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException(
                "Kafka topic is required.",
                nameof(topic));
        }

        var headers = new Headers
        {
            {
                "event-type",
                Encoding.UTF8.GetBytes(
                    message.EventType)
            },
            {
                "event-id",
                Encoding.UTF8.GetBytes(
                    message.Id.ToString())
            },
            {
                "correlation-id",
                Encoding.UTF8.GetBytes(
                    message.CorrelationId.ToString())
            }
        };

        if (message.CausationId.HasValue)
        {
            headers.Add(
                "causation-id",
                Encoding.UTF8.GetBytes(
                    message.CausationId.Value.ToString()));
        }

        KafkaTrace.Inject(headers);

        var kafkaMessage =
            new Message<string, string>
            {
                Key = message.Id.ToString(),
                Value = message.Payload,
                Headers = headers
            };

        await _producer.ProduceAsync(
            topic,
            kafkaMessage,
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _producer.Flush(
            TimeSpan.FromSeconds(5));

        _producer.Dispose();
    }
}