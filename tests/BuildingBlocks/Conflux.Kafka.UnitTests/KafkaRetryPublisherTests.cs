using System.Text;
using Confluent.Kafka;
using Conflux.Kafka;
using Microsoft.Extensions.Options;
using Xunit;

namespace Conflux.Kafka.UnitTests;

/// <summary>
/// Tests retry routing, consumer-specific isolation, and message metadata handling
/// for <see cref="KafkaRetryPublisher"/>.
/// </summary>
public sealed class KafkaRetryPublisherTests
{
    private const string SourceTopic = "conflux.catalog.events";

    /// <summary>
    /// Verifies that the first failure is routed to the first retry topic.
    /// </summary>
    [Fact]
    public async Task PublishFailureAsync_FirstFailure_RoutesToFirstRetryTopic()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = CreatePublisher(producer);

        await publisher.PublishFailureAsync(
            CreateResult(),
            new InvalidOperationException("temporary failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);

        Assert.Equal($"{SourceTopic}.retry.1", published.Topic);
        Assert.Equal("product-1", published.Message.Key);
        Assert.Equal("{\"sku\":\"TEST-001\"}", published.Message.Value);
        Assert.Equal(
            SourceTopic,
            ReadHeader(published.Message.Headers, "x-conflux-original-topic"));
        Assert.Equal(
            "1",
            ReadHeader(published.Message.Headers, "x-conflux-retry-attempt"));
        Assert.Equal(
            "temporary failure",
            ReadHeader(published.Message.Headers, "x-conflux-error"));
        Assert.NotNull(
            ReadHeader(published.Message.Headers, "x-conflux-not-before-unix-ms"));
    }

    /// <summary>
    /// Verifies that a second failure is routed to the second retry topic.
    /// </summary>
    [Fact]
    public async Task PublishFailureAsync_SecondFailure_RoutesToSecondRetryTopic()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = CreatePublisher(producer);

        await publisher.PublishFailureAsync(
            CreateResult(attempt: "1"),
            new InvalidOperationException("second failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);

        Assert.Equal($"{SourceTopic}.retry.2", published.Topic);
        Assert.Equal(
            "2",
            ReadHeader(published.Message.Headers, "x-conflux-retry-attempt"));
        Assert.NotNull(
            ReadHeader(published.Message.Headers, "x-conflux-not-before-unix-ms"));
    }

    /// <summary>
    /// Verifies that reaching the configured attempt limit routes the message
    /// to the dead-letter queue and removes retry scheduling metadata.
    /// </summary>
    [Fact]
    public async Task PublishFailureAsync_MaximumAttemptReached_RoutesToDeadLetterTopic()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = CreatePublisher(producer);

        await publisher.PublishFailureAsync(
            CreateResult(attempt: "2"),
            new InvalidOperationException("permanent failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);

        Assert.Equal($"{SourceTopic}.dlq", published.Topic);
        Assert.Equal(
            "3",
            ReadHeader(published.Message.Headers, "x-conflux-retry-attempt"));
        Assert.Equal(
            "permanent failure",
            ReadHeader(published.Message.Headers, "x-conflux-error"));
        Assert.NotNull(
            ReadHeader(published.Message.Headers, "x-conflux-dlq-attempted-at"));
        Assert.Null(
            ReadHeader(published.Message.Headers, "x-conflux-not-before-unix-ms"));
    }

    /// <summary>
    /// Verifies that a failure while processing a retry message preserves
    /// the original source topic when selecting the next retry destination.
    /// </summary>
    [Fact]
    public async Task PublishFailureAsync_RetryMessage_PreservesOriginalSourceTopic()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = CreatePublisher(producer);

        await publisher.PublishFailureAsync(
            CreateResult(
                topic: $"{SourceTopic}.retry.1",
                attempt: "1",
                originalTopic: SourceTopic),
            new InvalidOperationException("retry failed"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);

        Assert.Equal($"{SourceTopic}.retry.2", published.Topic);
        Assert.Equal(
            SourceTopic,
            ReadHeader(published.Message.Headers, "x-conflux-original-topic"));
    }

    /// <summary>
    /// Verifies that unrelated message headers are preserved and the source
    /// message headers are not mutated by publishing a failure.
    /// </summary>
    [Fact]
    public async Task PublishFailureAsync_PreservesExistingHeaders_WithoutMutatingSource()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = CreatePublisher(producer);

        var result = CreateResult();
        result.Message.Headers.Add(
            "trace-id",
            Encoding.UTF8.GetBytes("trace-123"));

        await publisher.PublishFailureAsync(
            result,
            new InvalidOperationException("failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);

        Assert.Equal(
            "trace-123",
            ReadHeader(published.Message.Headers, "trace-id"));
        Assert.Null(
            ReadHeader(result.Message.Headers, "x-conflux-retry-attempt"));
        Assert.Equal(
            "trace-123",
            ReadHeader(result.Message.Headers, "trace-id"));
    }

    /// <summary>
    /// Verifies that an invalid retry-attempt header is treated as the initial
    /// attempt and routes the message to the first retry topic.
    /// </summary>
    [Fact]
    public async Task PublishFailureAsync_InvalidAttemptHeader_RoutesToFirstRetryTopic()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = CreatePublisher(producer);

        await publisher.PublishFailureAsync(
            CreateResult(attempt: "invalid"),
            new InvalidOperationException("failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);

        Assert.Equal($"{SourceTopic}.retry.1", published.Topic);
        Assert.Equal(
            "1",
            ReadHeader(published.Message.Headers, "x-conflux-retry-attempt"));
    }

    /// <summary>
    /// Verifies that a configured retry identity isolates retry messages
    /// on a consumer-specific retry topic and is recorded in the message headers.
    /// </summary>
    [Fact]
    public async Task FirstFailure_WithRetryIdentity_UsesConsumerSpecificRetryTopic()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = new KafkaRetryPublisher(
            Options.Create(new KafkaRetryOptions
            {
                RetryIdentity = "fulfillment-order-confirmed",
                MaxAttempts = 3,
                BaseDelay = TimeSpan.FromMilliseconds(10)
            }),
            producer);

        await publisher.PublishFailureAsync(
            CreateResult(topic: "conflux.order.events"),
            new InvalidOperationException("temporary failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);

        Assert.Equal(
            "conflux.order.events.retry.fulfillment-order-confirmed.1",
            published.Topic);

        Assert.Equal(
            "fulfillment-order-confirmed",
            ReadHeader(published.Message.Headers, "x-conflux-retry-identity"));
    }

    /// <summary>
    /// Verifies that reaching the maximum attempt count routes the message
    /// to the dead-letter queue associated with its configured retry identity.
    /// </summary>
    [Fact]
    public async Task MaximumAttempt_WithRetryIdentity_UsesConsumerSpecificDlq()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = new KafkaRetryPublisher(
            Options.Create(new KafkaRetryOptions
            {
                RetryIdentity = "order-checkout-saga",
                MaxAttempts = 3,
                BaseDelay = TimeSpan.FromMilliseconds(10)
            }),
            producer);

        await publisher.PublishFailureAsync(
            CreateResult(
                topic: "conflux.order.events",
                attempt: "2"),
            new InvalidOperationException("permanent failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);

        Assert.Equal(
            "conflux.order.events.dlq.order-checkout-saga",
            published.Topic);

        Assert.Equal(
            "order-checkout-saga",
            ReadHeader(published.Message.Headers, "x-conflux-retry-identity"));
    }

    /// <summary>
    /// Verifies that configured identity takes precedence over a message header.
    /// </summary>
    [Fact]
    public async Task PublishFailureAsync_ConfiguredIdentityOverridesMessageIdentity()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = new KafkaRetryPublisher(
            Options.Create(new KafkaRetryOptions
            {
                RetryIdentity = "catalog-projection",
                MaxAttempts = 3,
                BaseDelay = TimeSpan.FromMilliseconds(10)
            }),
            producer);

        var result = CreateResult();
        result.Message.Headers.Add(
            "x-conflux-retry-identity",
            Encoding.UTF8.GetBytes("untrusted-identity"));

        await publisher.PublishFailureAsync(
            result,
            new InvalidOperationException("temporary failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);
        Assert.Equal(
            $"{SourceTopic}.retry.catalog-projection.1",
            published.Topic);
        Assert.Equal(
            "catalog-projection",
            ReadHeader(published.Message.Headers, "x-conflux-retry-identity"));
    }

    /// <summary>
    /// Verifies that an unconfigured original-topic header cannot redirect
    /// retry publication to an arbitrary Kafka topic.
    /// </summary>
    [Fact]
    public async Task PublishFailureAsync_UnconfiguredOriginalTopic_UsesConsumedTopic()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = new KafkaRetryPublisher(
            Options.Create(new KafkaRetryOptions
            {
                SourceTopics = [SourceTopic],
                RetryIdentity = "catalog-projection",
                MaxAttempts = 3,
                BaseDelay = TimeSpan.FromMilliseconds(10)
            }),
            producer);

        await publisher.PublishFailureAsync(
            CreateResult(
                topic: SourceTopic,
                originalTopic: "conflux.untrusted.events"),
            new InvalidOperationException("temporary failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);
        Assert.Equal(
            $"{SourceTopic}.retry.catalog-projection.1",
            published.Topic);
        Assert.Equal(
            SourceTopic,
            ReadHeader(published.Message.Headers, "x-conflux-original-topic"));
    }

    /// <summary>
    /// Creates a publisher backed by a fake producer and the default
    /// legacy-compatible retry topic configuration.
    /// </summary>
    /// <param name="producer">The fake producer that records published messages.</param>
    /// <returns>A publisher configured for the tests.</returns>
    private static KafkaRetryPublisher CreatePublisher(
        FakeKafkaMessageProducer producer)
    {
        var options = Options.Create(new KafkaRetryOptions
        {
            BootstrapServers = "unused:9092",
            SourceTopics = [SourceTopic],
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(10)
        });

        return new KafkaRetryPublisher(options, producer);
    }

    /// <summary>
    /// Creates a representative consumed Kafka message with optional retry
    /// attempt and original-topic headers.
    /// </summary>
    /// <param name="topic">The topic from which the message was consumed.</param>
    /// <param name="attempt">The optional retry-attempt header value.</param>
    /// <param name="originalTopic">The optional original source-topic header value.</param>
    /// <returns>A synthetic Kafka consume result for unit testing.</returns>
    private static ConsumeResult<string, string> CreateResult(
        string topic = SourceTopic,
        string? attempt = null,
        string? originalTopic = null)
    {
        var headers = new Headers();

        if (attempt is not null)
        {
            headers.Add(
                "x-conflux-retry-attempt",
                Encoding.UTF8.GetBytes(attempt));
        }

        if (originalTopic is not null)
        {
            headers.Add(
                "x-conflux-original-topic",
                Encoding.UTF8.GetBytes(originalTopic));
        }

        return new ConsumeResult<string, string>
        {
            Topic = topic,
            Partition = new Partition(0),
            Offset = new Offset(42),
            Message = new Message<string, string>
            {
                Key = "product-1",
                Value = "{\"sku\":\"TEST-001\"}",
                Headers = headers,
                Timestamp = new Timestamp(DateTime.UtcNow)
            }
        };
    }

    /// <summary>
    /// Reads the last occurrence of a named Kafka header and decodes its value
    /// as UTF-8 text.
    /// </summary>
    /// <param name="headers">The headers to inspect.</param>
    /// <param name="name">The header name to find.</param>
    /// <returns>The decoded header value, or <see langword="null"/> if absent.</returns>
    private static string? ReadHeader(Headers headers, string name)
    {
        for (var index = headers.Count - 1; index >= 0; index--)
        {
            if (headers[index].Key == name)
            {
                var bytes = headers[index].GetValueBytes();
                return bytes is null
                    ? null
                    : Encoding.UTF8.GetString(bytes);
            }
        }

        return null;
    }
}