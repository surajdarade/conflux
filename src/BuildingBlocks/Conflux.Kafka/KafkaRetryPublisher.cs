using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Conflux.Kafka;

/// <summary>Publishes failed Kafka messages to retry topics or a DLQ.</summary>
public sealed class KafkaRetryPublisher : IDisposable
{
    private const string RetryIdentityHeader = "x-conflux-retry-identity";

    private readonly IKafkaMessageProducer _producer;
    private readonly KafkaRetryOptions _options;

    /// <summary>Initializes the retry publisher with a configured Kafka producer.</summary>
    public KafkaRetryPublisher(IOptions<KafkaRetryOptions> options)
        : this(options, CreateProducer(options))
    {
    }

    /// <summary>Initializes the retry publisher with an injectable message producer.</summary>
    public KafkaRetryPublisher(
        IOptions<KafkaRetryOptions> options,
        IKafkaMessageProducer producer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(producer);

        _options = options.Value;
        _producer = producer;
    }

    /// <summary>Routes a failed message to its next retry stage or DLQ.</summary>
    public async Task PublishFailureAsync(
        ConsumeResult<string, string> result,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(exception);

        var headers = CloneHeaders(result.Message.Headers);

        var sourceTopic = Header(headers, "x-conflux-original-topic");
        if (string.IsNullOrWhiteSpace(sourceTopic) ||
            (_options.SourceTopics.Count > 0 &&
             !_options.SourceTopics.Contains(sourceTopic, StringComparer.Ordinal)))
        {
            // The consumed topic is broker metadata; an original-topic header is
            // message metadata and must not redirect failures to arbitrary topics.
            if (_options.SourceTopics.Count > 0 &&
                !_options.SourceTopics.Contains(result.Topic, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot route a failed Kafka message from unconfigured topic '{result.Topic}'.");
            }

            sourceTopic = result.Topic;
        }

        // A configured identity is authoritative. Message metadata is only a
        // compatibility fallback for a publisher running in legacy mode.
        var retryIdentity = string.IsNullOrWhiteSpace(_options.RetryIdentity)
            ? Header(headers, RetryIdentityHeader)
            : _options.RetryIdentity;

        var currentAttempt = ParseAttempt(headers);
        var nextAttempt = currentAttempt + 1;

        var destination = nextAttempt >= _options.MaxAttempts
            ? GetDlqTopic(sourceTopic, retryIdentity)
            : GetRetryTopic(sourceTopic, retryIdentity, nextAttempt);

        SetHeader(headers, "x-conflux-original-topic", sourceTopic);
        SetHeader(headers, "x-conflux-retry-attempt", nextAttempt.ToString());
        SetHeader(headers, "x-conflux-error", exception.Message);

        if (!string.IsNullOrWhiteSpace(retryIdentity))
        {
            SetHeader(headers, RetryIdentityHeader, retryIdentity);
        }

        if (destination.Contains(".retry.", StringComparison.Ordinal))
        {
            var delay = TimeSpan.FromMilliseconds(
                _options.BaseDelay.TotalMilliseconds *
                Math.Pow(2, Math.Max(0, nextAttempt - 1)));

            SetHeader(
                headers,
                "x-conflux-not-before-unix-ms",
                DateTimeOffset.UtcNow.Add(delay).ToUnixTimeMilliseconds().ToString());
        }
        else
        {
            headers.Remove("x-conflux-not-before-unix-ms");
            SetHeader(
                headers,
                "x-conflux-dlq-attempted-at",
                DateTimeOffset.UtcNow.ToString("O"));
        }

        await _producer.ProduceAsync(
            destination,
            new Message<string, string>
            {
                Key = result.Message.Key,
                Value = result.Message.Value,
                Headers = headers,
                Timestamp = result.Message.Timestamp
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _producer.Dispose();

    private string GetRetryTopic(string sourceTopic, string retryIdentity, int attempt) =>
        string.IsNullOrWhiteSpace(retryIdentity)
            ? KafkaRetryOptions.GetRetryTopic(sourceTopic, attempt)
            : KafkaRetryOptions.GetRetryTopic(sourceTopic, retryIdentity, attempt);

    private string GetDlqTopic(string sourceTopic, string retryIdentity) =>
        string.IsNullOrWhiteSpace(retryIdentity)
            ? KafkaRetryOptions.GetDlqTopic(sourceTopic)
            : KafkaRetryOptions.GetDlqTopic(sourceTopic, retryIdentity);

    private static int ParseAttempt(Headers headers)
    {
        var value = Header(headers, "x-conflux-retry-attempt");
        return int.TryParse(value, out var attempt) && attempt >= 0 ? attempt : 0;
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

    private static IKafkaMessageProducer CreateProducer(
        IOptions<KafkaRetryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ConfluentKafkaMessageProducer(options.Value);
    }
}

/// <summary>Publishes messages using the Confluent Kafka producer.</summary>
internal sealed class ConfluentKafkaMessageProducer : IKafkaMessageProducer
{
    private readonly IProducer<string, string> _producer;

    /// <summary>Creates a Kafka producer using the retry configuration.</summary>
    public ConfluentKafkaMessageProducer(KafkaRetryOptions options)
    {
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            MessageTimeoutMs = 30_000
        }).Build();
    }

    /// <inheritdoc />
    public async Task ProduceAsync(
        string topic,
        Message<string, string> message,
        CancellationToken cancellationToken)
    {
        await _producer.ProduceAsync(topic, message, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _producer.Dispose();
}