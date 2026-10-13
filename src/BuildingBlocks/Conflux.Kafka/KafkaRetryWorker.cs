using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conflux.Kafka;

/// <summary>Replays retry-topic messages to their original source topic when eligible.</summary>
public sealed class KafkaRetryWorker : BackgroundService
{
    private readonly KafkaRetryOptions _options;
    private readonly ILogger<KafkaRetryWorker> _logger;
    private readonly Func<IKafkaMessageConsumer> _consumerFactory;
    private readonly Func<IKafkaMessageProducer> _producerFactory;

    /// <summary>Initializes the retry worker with configured Kafka clients.</summary>
    public KafkaRetryWorker(
        IOptions<KafkaRetryOptions> options,
        ILogger<KafkaRetryWorker> logger)
        : this(
            options,
            logger,
            () => CreateConsumer(options.Value),
            () => new ConfluentKafkaMessageProducer(options.Value))
    {
    }

    /// <summary>Initializes the retry worker with injectable Kafka client factories.</summary>
    public KafkaRetryWorker(
        IOptions<KafkaRetryOptions> options,
        ILogger<KafkaRetryWorker> logger,
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
        if (_options.SourceTopics.Count == 0)
        {
            return;
        }

        var retryTopics = _options.SourceTopics
            .SelectMany(source => Enumerable.Range(1, Math.Max(1, _options.MaxAttempts - 1))
                .Select(attempt => KafkaRetryOptions.GetRetryTopic(source, attempt)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        using var consumer = _consumerFactory();
        using var producer = _producerFactory();
        consumer.Subscribe(retryTopics);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;

                try
                {
                    result = consumer.Consume(stoppingToken);

                    var dueAt = ParseDueAt(result.Message.Headers);
                    var delay = dueAt - DateTimeOffset.UtcNow;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, stoppingToken);
                    }

                    var sourceTopic = Header(
                        result.Message.Headers,
                        "x-conflux-original-topic");

                    if (string.IsNullOrWhiteSpace(sourceTopic))
                    {
                        _logger.LogError(
                            "Retry message {Topic}/{Partition}/{Offset} has no original topic; sending it to DLQ.",
                            result.Topic,
                            result.Partition,
                            result.Offset);

                        await producer.ProduceAsync(
                            KafkaRetryOptions.GetDlqTopic(result.Topic),
                            result.Message,
                            stoppingToken);

                        consumer.Commit(result);
                        continue;
                    }

                    var headers = CloneHeaders(result.Message.Headers);
                    headers.Remove("x-conflux-not-before-unix-ms");

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
                catch (ConsumeException exception)
                {
                    _logger.LogError(exception, "Kafka retry worker consumption failed.");
                    await Task.Delay(_options.PollInterval, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
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
            GroupId = options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true
        };

        var consumer = new ConsumerBuilder<string, string>(config).Build();
        return new ConfluentKafkaMessageConsumer(consumer);
    }

    private static DateTimeOffset ParseDueAt(Headers headers)
    {
        var value = Header(headers, "x-conflux-not-before-unix-ms");
        return long.TryParse(value, out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : DateTimeOffset.UtcNow;
    }

    private static Headers CloneHeaders(Headers headers)
    {
        var clone = new Headers();
        foreach (var header in headers)
        {
            clone.Add(header.Key, header.GetValueBytes());
        }

        return clone;
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
