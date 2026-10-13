using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conflux.Kafka;

/// <summary>
/// Performs one controlled automatic recovery attempt for dead-letter messages.
/// Messages that fail again are moved to a permanent DLQ topic.
/// </summary>
public sealed class KafkaDlqRecoveryWorker : BackgroundService
{
    private readonly KafkaRetryOptions _options;
    private readonly ILogger<KafkaDlqRecoveryWorker> _logger;
    private readonly Func<IKafkaMessageConsumer> _consumerFactory;
    private readonly Func<IKafkaMessageProducer> _producerFactory;

    /// <summary>Initializes the recovery worker with configured Kafka clients.</summary>
    public KafkaDlqRecoveryWorker(
        IOptions<KafkaRetryOptions> options,
        ILogger<KafkaDlqRecoveryWorker> logger)
        : this(
            options,
            logger,
            () => CreateConsumer(options.Value),
            () => new ConfluentKafkaMessageProducer(options.Value))
    {
    }

    /// <summary>Initializes the recovery worker with injectable Kafka client factories.</summary>
    public KafkaDlqRecoveryWorker(
        IOptions<KafkaRetryOptions> options,
        ILogger<KafkaDlqRecoveryWorker> logger,
        Func<IKafkaMessageConsumer> consumerFactory,
        Func<IKafkaMessageProducer> producerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(consumerFactory);
        ArgumentNullException.ThrowIfNull(producerFactory);

        _options = options.Value;
        _logger = logger;
        _consumerFactory = consumerFactory;
        _producerFactory = producerFactory;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.AutomaticDlqRecovery || _options.SourceTopics.Count == 0)
        {
            return;
        }

        var dlqTopics = _options.SourceTopics
            .Select(KafkaRetryOptions.GetDlqTopic)
            .ToArray();

        using var consumer = _consumerFactory();
        using var producer = _producerFactory();

        consumer.Subscribe(dlqTopics);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                var recovered = Header(result.Message.Headers, "x-conflux-recovered");

                if (string.Equals(recovered, "true", StringComparison.OrdinalIgnoreCase))
                {
                    await producer.ProduceAsync(
                        PermanentDlqTopic(result.Topic),
                        result.Message,
                        stoppingToken);

                    consumer.Commit(result);
                    continue;
                }

                await Task.Delay(_options.DlqRecoveryDelay, stoppingToken);

                var sourceTopic = Header(
                    result.Message.Headers,
                    "x-conflux-original-topic");

                if (string.IsNullOrWhiteSpace(sourceTopic))
                {
                    await producer.ProduceAsync(
                        PermanentDlqTopic(result.Topic),
                        result.Message,
                        stoppingToken);

                    consumer.Commit(result);
                    continue;
                }

                var headers = CloneHeaders(result.Message.Headers);
                headers.Remove("x-conflux-not-before-unix-ms");
                headers.Remove("x-conflux-retry-attempt");

                SetHeader(headers, "x-conflux-retry-attempt", "0");
                SetHeader(headers, "x-conflux-recovered", "true");
                SetHeader(
                    headers,
                    "x-conflux-recovered-at",
                    DateTimeOffset.UtcNow.ToString("O"));

                await producer.ProduceAsync(
                    sourceTopic,
                    new Message<string, string>
                    {
                        Key = result.Message.Key,
                        Value = result.Message.Value,
                        Headers = headers,
                        Timestamp = result.Message.Timestamp
                    },
                    stoppingToken);

                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Kafka DLQ recovery worker stopped unexpectedly.");

            throw;
        }
        finally
        {
            consumer.Close();
        }
    }

    private static IKafkaMessageConsumer CreateConsumer(KafkaRetryOptions options)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = options.ConsumerGroup + "-dlq-recovery",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true
        };

        var consumer = new ConsumerBuilder<string, string>(config).Build();
        return new ConfluentKafkaMessageConsumer(consumer);
    }

    private static string PermanentDlqTopic(string dlqTopic) =>
        dlqTopic + ".permanent";

    private static Headers CloneHeaders(Headers headers)
    {
        var clone = new Headers();

        foreach (var header in headers)
        {
            clone.Add(header.Key, header.GetValueBytes());
        }

        return clone;
    }

    private static void SetHeader(Headers headers, string key, string value)
    {
        headers.Remove(key);
        headers.Add(key, Encoding.UTF8.GetBytes(value));
    }

    private static string Header(Headers headers, string name)
    {
        for (var index = headers.Count - 1; index >= 0; index--)
        {
            if (string.Equals(headers[index].Key, name, StringComparison.Ordinal))
            {
                var bytes = headers[index].GetValueBytes();

                return bytes is { Length: > 0 }
                    ? Encoding.UTF8.GetString(bytes)
                    : string.Empty;
            }
        }

        return string.Empty;
    }
}
