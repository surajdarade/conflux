using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Conflux.Kafka;

/// <summary>Publishes failed Kafka messages to retry topics or a DLQ.</summary>
public sealed class KafkaRetryPublisher : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaRetryOptions _options;

    /// <summary>Initializes the retry publisher.</summary>
    public KafkaRetryPublisher(IOptions<KafkaRetryOptions> options)
    {
        _options = options.Value;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            MessageTimeoutMs = 30_000
        }).Build();
    }

    /// <summary>Routes a failed message to its next retry stage or DLQ.</summary>
    public async Task PublishFailureAsync(
        ConsumeResult<string, string> result,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var sourceTopic = Header(result.Message.Headers, "x-conflux-original-topic");
        if (string.IsNullOrWhiteSpace(sourceTopic))
        {
            sourceTopic = result.Topic;
        }

        var currentAttempt = ParseAttempt(result.Message.Headers);
        var nextAttempt = currentAttempt + 1;
        var destination = nextAttempt >= _options.MaxAttempts
            ? KafkaRetryOptions.GetDlqTopic(sourceTopic)
            : KafkaRetryOptions.GetRetryTopic(sourceTopic, nextAttempt);

        var headers = CloneHeaders(result.Message.Headers);
        SetHeader(headers, "x-conflux-original-topic", sourceTopic);
        SetHeader(headers, "x-conflux-retry-attempt", nextAttempt.ToString());
        SetHeader(headers, "x-conflux-error", exception.Message);

        if (destination.Contains(".retry.", StringComparison.Ordinal))
        {
            var delay = TimeSpan.FromMilliseconds(
                _options.BaseDelay.TotalMilliseconds * Math.Pow(2, Math.Max(0, nextAttempt - 1)));
            SetHeader(
                headers,
                "x-conflux-not-before-unix-ms",
                DateTimeOffset.UtcNow.Add(delay).ToUnixTimeMilliseconds().ToString());
        }
        else
        {
            SetHeader(headers, "x-conflux-dlq-attempted-at", DateTimeOffset.UtcNow.ToString("O"));
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
                return bytes is { Length: > 0 } ? Encoding.UTF8.GetString(bytes) : string.Empty;
            }
        }
        return string.Empty;
    }
}
