using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conflux.Kafka;

/// <summary>
/// Replays eligible retry-topic messages to their original source topics.
/// Supports consumer-specific retry-topic isolation while retaining
/// compatibility with legacy retry-topic naming.
/// </summary>
public sealed class KafkaRetryWorker : BackgroundService
{
    private const string OriginalTopicHeader = "x-conflux-original-topic";
    private const string RetryIdentityHeader = "x-conflux-retry-identity";
    private const string RetryAttemptHeader = "x-conflux-retry-attempt";
    private const string NotBeforeHeader = "x-conflux-not-before-unix-ms";

    private readonly KafkaRetryOptions _options;
    private readonly ILogger<KafkaRetryWorker> _logger;
    private readonly Func<IKafkaMessageConsumer> _consumerFactory;
    private readonly Func<IKafkaMessageProducer> _producerFactory;

    /// <summary>
    /// Initializes the worker using Kafka clients created from the configured options.
    /// </summary>
    /// <param name="options">Retry worker configuration.</param>
    /// <param name="logger">Logger for retry processing diagnostics.</param>
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

    /// <summary>
    /// Initializes the worker with injectable Kafka client factories for testing.
    /// </summary>
    /// <param name="options">Retry worker configuration.</param>
    /// <param name="logger">Logger for retry processing diagnostics.</param>
    /// <param name="consumerFactory">Factory for creating the retry-topic consumer.</param>
    /// <param name="producerFactory">Factory for creating the source-topic producer.</param>
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
            _logger.LogInformation(
                "Kafka retry worker is not starting because no source topics are configured.");
            return;
        }

        var retryTopics = _options.SourceTopics
            .SelectMany(source => Enumerable.Range(
                    1,
                    Math.Max(0, _options.MaxAttempts - 1))
                .Select(attempt => GetRetryTopic(source, attempt)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (retryTopics.Length == 0)
        {
            _logger.LogInformation(
                "Kafka retry worker is not starting because no retry topics are configured.");
            return;
        }

        using var consumer = _consumerFactory();
        using var producer = _producerFactory();

        consumer.Subscribe(retryTopics);

        _logger.LogInformation(
            "Kafka retry worker subscribed to {RetryTopicCount} topics with retry identity {RetryIdentity}.",
            retryTopics.Length,
            string.IsNullOrWhiteSpace(_options.RetryIdentity)
                ? "(legacy)"
                : _options.RetryIdentity);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);

                    if (result?.Message is null)
                    {
                        continue;
                    }

                    if (!HasExpectedRetryIdentity(result))
                    {
                        await RouteInvalidIdentityAsync(
                            result,
                            producer,
                            stoppingToken);

                        consumer.Commit(result);
                        continue;
                    }

                    var dueAt = ParseDueAt(result.Message.Headers);
                    var delay = dueAt - DateTimeOffset.UtcNow;

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, stoppingToken);
                    }

                    var sourceTopic = Header(
                        result.Message.Headers,
                        OriginalTopicHeader);

                    if (string.IsNullOrWhiteSpace(sourceTopic))
                    {
                        _logger.LogError(
                            "Retry message {Topic}/{Partition}/{Offset} has no original topic; sending it to the DLQ.",
                            result.Topic,
                            result.Partition,
                            result.Offset);

                        await producer.ProduceAsync(
                            GetFallbackDlqTopic(result.Topic),
                            result.Message,
                            stoppingToken);

                        consumer.Commit(result);
                        continue;
                    }

                    if (!_options.SourceTopics.Contains(
                            sourceTopic,
                            StringComparer.Ordinal))
                    {
                        _logger.LogError(
                            "Retry message {Topic}/{Partition}/{Offset} references unconfigured source topic {SourceTopic}; sending it to the DLQ.",
                            result.Topic,
                            result.Partition,
                            result.Offset,
                            sourceTopic);

                        // Do not derive a publish destination from an untrusted
                        // original-topic header. Quarantine under the retry topic
                        // this worker actually consumed.
                        await producer.ProduceAsync(
                            GetFallbackDlqTopic(result.Topic),
                            result.Message,
                            stoppingToken);

                        consumer.Commit(result);
                        continue;
                    }

                    var headers = CloneHeaders(result.Message.Headers);
                    headers.Remove(NotBeforeHeader);

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

                    // Commit only after successful publication. If publication succeeds
                    // but committing fails, replaying the record can produce duplicates.
                    consumer.Commit(result);
                }
                catch (ConsumeException exception)
                {
                    _logger.LogError(
                        exception,
                        "Kafka retry worker consumption failed.");

                    await Task.Delay(_options.PollInterval, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown: the host requested cancellation.
        }
        finally
        {
            consumer.Close();
        }
    }

    /// <summary>
    /// Builds the retry topic name using the configured identity, or the legacy
    /// topic name when no identity is configured.
    /// </summary>
    /// <param name="sourceTopic">Original source topic.</param>
    /// <param name="attempt">One-based retry attempt.</param>
    /// <returns>The retry topic name.</returns>
    private string GetRetryTopic(string sourceTopic, int attempt)
    {
        return string.IsNullOrWhiteSpace(_options.RetryIdentity)
            ? KafkaRetryOptions.GetRetryTopic(sourceTopic, attempt)
            : KafkaRetryOptions.GetRetryTopic(
                sourceTopic,
                _options.RetryIdentity,
                attempt);
    }

    /// <summary>
    /// Builds the dead-letter topic name using the configured identity, or
    /// the legacy topic name when no identity is configured.
    /// </summary>
    /// <param name="sourceTopic">Original source topic.</param>
    /// <returns>The dead-letter topic name.</returns>
    private string GetDlqTopic(string sourceTopic)
    {
        return string.IsNullOrWhiteSpace(_options.RetryIdentity)
            ? KafkaRetryOptions.GetDlqTopic(sourceTopic)
            : KafkaRetryOptions.GetDlqTopic(
                sourceTopic,
                _options.RetryIdentity);
    }

    /// <summary>
    /// Determines whether a retry message belongs to this worker's configured
    /// retry identity. Legacy mode accepts messages without an identity header.
    /// </summary>
    /// <param name="result">Consumed retry message.</param>
    /// <returns>
    /// <see langword="true"/> if the message belongs to this worker;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    private bool HasExpectedRetryIdentity(
        ConsumeResult<string, string> result)
    {
        if (string.IsNullOrWhiteSpace(_options.RetryIdentity))
        {
            return true;
        }

        var messageIdentity = Header(
            result.Message.Headers,
            RetryIdentityHeader);

        return string.Equals(
            messageIdentity,
            _options.RetryIdentity,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Routes a retry message with missing or mismatched identity metadata to
    /// this worker's DLQ without replaying it to the source topic.
    /// </summary>
    /// <param name="result">Consumed retry message.</param>
    /// <param name="producer">Producer used to publish the dead-letter message.</param>
    /// <param name="cancellationToken">Cancellation token for publication.</param>
    private async Task RouteInvalidIdentityAsync(
        ConsumeResult<string, string> result,
        IKafkaMessageProducer producer,
        CancellationToken cancellationToken)
    {
        var sourceTopic = Header(
            result.Message.Headers,
            OriginalTopicHeader);

        var destination = !string.IsNullOrWhiteSpace(sourceTopic) &&
                          _options.SourceTopics.Contains(
                              sourceTopic,
                              StringComparer.Ordinal)
            ? GetDlqTopic(sourceTopic)
            : GetFallbackDlqTopic(result.Topic);

        _logger.LogError(
            "Retry message {Topic}/{Partition}/{Offset} has a missing or mismatched retry identity. Expected {ExpectedIdentity}; received {ActualIdentity}. Routing to {Destination}.",
            result.Topic,
            result.Partition,
            result.Offset,
            _options.RetryIdentity,
            Header(result.Message.Headers, RetryIdentityHeader),
            destination);

        await producer.ProduceAsync(
            destination,
            result.Message,
            cancellationToken);
    }

    /// <summary>
    /// Chooses a fallback DLQ for a malformed retry message whose original
    /// source topic cannot be determined.
    /// </summary>
    /// <param name="retryTopic">Topic containing the malformed message.</param>
    /// <returns>The fallback dead-letter topic.</returns>
    private string GetFallbackDlqTopic(string retryTopic)
    {
        return string.IsNullOrWhiteSpace(_options.RetryIdentity)
            ? KafkaRetryOptions.GetDlqTopic(retryTopic)
            : $"{KafkaRetryOptions.GetDlqTopic(retryTopic)}.{_options.RetryIdentity}";
    }

    /// <summary>
    /// Creates a Kafka consumer configured for manual offset commits.
    /// </summary>
    /// <param name="options">Retry worker configuration.</param>
    /// <returns>A consumer adapter.</returns>
    private static IKafkaMessageConsumer CreateConsumer(
        KafkaRetryOptions options)
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

    /// <summary>
    /// Reads the retry scheduling header and converts it to a UTC due time.
    /// Missing or invalid values make the message immediately eligible.
    /// </summary>
    /// <param name="headers">Headers attached to the retry message.</param>
    /// <returns>The UTC time when the message can be replayed.</returns>
    private static DateTimeOffset ParseDueAt(Headers headers)
    {
        var value = Header(headers, NotBeforeHeader);

        return long.TryParse(value, out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Copies Kafka headers so retry processing does not mutate the consumed message.
    /// </summary>
    /// <param name="headers">Original message headers.</param>
    /// <returns>A new collection containing the same headers.</returns>
    private static Headers CloneHeaders(Headers headers)
    {
        var clone = new Headers();

        foreach (var header in headers)
        {
            clone.Add(header.Key, header.GetValueBytes());
        }

        return clone;
    }

    /// <summary>
    /// Reads the last occurrence of a header and decodes its value as UTF-8.
    /// </summary>
    /// <param name="headers">Headers to inspect.</param>
    /// <param name="name">Header name.</param>
    /// <returns>The decoded value, or an empty string when absent or empty.</returns>
    private static string Header(Headers headers, string name)
    {
        for (var index = headers.Count - 1; index >= 0; index--)
        {
            if (string.Equals(
                    headers[index].Key,
                    name,
                    StringComparison.Ordinal))
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