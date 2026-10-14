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
    private const string RetryIdentityHeader = "x-conflux-retry-identity";
    private const string OriginalTopicHeader = "x-conflux-original-topic";
    private const string RecoveredHeader = "x-conflux-recovered";
    private const string RetryAttemptHeader = "x-conflux-retry-attempt";
    private const string NotBeforeHeader = "x-conflux-not-before-unix-ms";
    private const string RecoveredAtHeader = "x-conflux-recovered-at";

    private readonly KafkaRetryOptions _options;
    private readonly ILogger<KafkaDlqRecoveryWorker> _logger;
    private readonly Func<IKafkaMessageConsumer> _consumerFactory;
    private readonly Func<IKafkaMessageProducer> _producerFactory;

    /// <summary>
    /// Initializes the recovery worker with configured Kafka clients.
    /// </summary>
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

    /// <summary>
    /// Initializes the recovery worker with injectable Kafka client factories.
    /// </summary>
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
            .Select(GetDlqTopic)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        using var consumer = _consumerFactory();
        using var producer = _producerFactory();

        consumer.Subscribe(dlqTopics);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                var message = result.Message;

                // An identity-configured worker must not recover messages
                // belonging to another consumer or lacking identity metadata.
                if (!HasExpectedIdentity(message.Headers))
                {
                    _logger.LogWarning(
                        "Kafka DLQ recovery rejected a message from {DlqTopic} " +
                        "because its retry identity was missing or mismatched.",
                        result.Topic);

                    await MoveToPermanentDlqAsync(
                        result.Topic,
                        message,
                        producer,
                        stoppingToken);

                    consumer.Commit(result);
                    continue;
                }

                // Prevent automatic recovery loops.
                if (IsAlreadyRecovered(message.Headers))
                {
                    await MoveToPermanentDlqAsync(
                        result.Topic,
                        message,
                        producer,
                        stoppingToken);

                    consumer.Commit(result);
                    continue;
                }

                await Task.Delay(_options.DlqRecoveryDelay, stoppingToken);

                var sourceTopic = Header(message.Headers, OriginalTopicHeader);

                // Never replay to a topic supplied only by untrusted message
                // metadata. The destination must be explicitly configured.
                if (string.IsNullOrWhiteSpace(sourceTopic) ||
                    !_options.SourceTopics.Contains(
                        sourceTopic,
                        StringComparer.Ordinal))
                {
                    _logger.LogWarning(
                        "Kafka DLQ recovery rejected a message from {DlqTopic}: " +
                        "original topic {OriginalTopic} is missing or not configured.",
                        result.Topic,
                        sourceTopic);

                    await MoveToPermanentDlqAsync(
                        result.Topic,
                        message,
                        producer,
                        stoppingToken);

                    consumer.Commit(result);
                    continue;
                }

                var headers = CloneHeaders(message.Headers);

                headers.Remove(NotBeforeHeader);
                headers.Remove(RetryAttemptHeader);

                SetHeader(headers, RetryAttemptHeader, "0");
                SetHeader(headers, RecoveredHeader, "true");
                SetHeader(
                    headers,
                    RecoveredAtHeader,
                    DateTimeOffset.UtcNow.ToString("O"));

                // Preserve the retry identity and original message metadata.
                await producer.ProduceAsync(
                    sourceTopic,
                    new Message<string, string>
                    {
                        Key = message.Key,
                        Value = message.Value,
                        Headers = headers,
                        Timestamp = message.Timestamp
                    },
                    stoppingToken);

                // Commit only after publication succeeds.
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

    private string GetDlqTopic(string sourceTopic) =>
        string.IsNullOrWhiteSpace(_options.RetryIdentity)
            ? KafkaRetryOptions.GetDlqTopic(sourceTopic)
            : KafkaRetryOptions.GetDlqTopic(
                sourceTopic,
                _options.RetryIdentity);

    private bool HasExpectedIdentity(Headers headers)
    {
        // An empty identity preserves the legacy topic-naming behavior.
        if (string.IsNullOrWhiteSpace(_options.RetryIdentity))
        {
            return true;
        }

        return string.Equals(
            Header(headers, RetryIdentityHeader),
            _options.RetryIdentity,
            StringComparison.Ordinal);
    }

    private static bool IsAlreadyRecovered(Headers headers) =>
        string.Equals(
            Header(headers, RecoveredHeader),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static Task MoveToPermanentDlqAsync(
        string dlqTopic,
        Message<string, string> message,
        IKafkaMessageProducer producer,
        CancellationToken cancellationToken) =>
        producer.ProduceAsync(
            PermanentDlqTopic(dlqTopic),
            message,
            cancellationToken);

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