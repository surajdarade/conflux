using System.Text;
using Confluent.Kafka;
using Conflux.Kafka;
using Microsoft.Extensions.Options;
using Xunit;

namespace Conflux.Kafka.UnitTests;

/// <summary>
/// Tests retry routing and message metadata handling.
/// </summary>
public sealed class KafkaRetryPublisherTests
{
    private const string SourceTopic = "conflux.catalog.events";

    /// <summary>Routes a first failure to the first retry topic.</summary>
    [Fact]
    public async Task FirstFailure_RoutesToFirstRetryTopic()
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
        Assert.Equal(SourceTopic, ReadHeader(published.Message.Headers, "x-conflux-original-topic"));
        Assert.Equal("1", ReadHeader(published.Message.Headers, "x-conflux-retry-attempt"));
        Assert.Equal("temporary failure", ReadHeader(published.Message.Headers, "x-conflux-error"));
        Assert.NotNull(ReadHeader(published.Message.Headers, "x-conflux-not-before-unix-ms"));
    }

    /// <summary>Routes a second failure to the second retry topic.</summary>
    [Fact]
    public async Task SecondFailure_RoutesToSecondRetryTopic()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = CreatePublisher(producer);

        await publisher.PublishFailureAsync(
            CreateResult(attempt: "1"),
            new InvalidOperationException("second failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);
        Assert.Equal($"{SourceTopic}.retry.2", published.Topic);
        Assert.Equal("2", ReadHeader(published.Message.Headers, "x-conflux-retry-attempt"));
        Assert.NotNull(ReadHeader(published.Message.Headers, "x-conflux-not-before-unix-ms"));
    }

    /// <summary>Routes the maximum-attempt failure to the dead-letter topic.</summary>
    [Fact]
    public async Task MaximumAttemptReached_RoutesToDeadLetterTopic()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = CreatePublisher(producer);

        await publisher.PublishFailureAsync(
            CreateResult(attempt: "2"),
            new InvalidOperationException("permanent failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);
        Assert.Equal($"{SourceTopic}.dlq", published.Topic);
        Assert.Equal("3", ReadHeader(published.Message.Headers, "x-conflux-retry-attempt"));
        Assert.Equal("permanent failure", ReadHeader(published.Message.Headers, "x-conflux-error"));
        Assert.NotNull(ReadHeader(published.Message.Headers, "x-conflux-dlq-attempted-at"));
        Assert.Null(ReadHeader(published.Message.Headers, "x-conflux-not-before-unix-ms"));
    }

    /// <summary>Uses the original-topic header when processing a retry message.</summary>
    [Fact]
    public async Task RetryMessage_PreservesOriginalSourceTopic()
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
        Assert.Equal(SourceTopic, ReadHeader(published.Message.Headers, "x-conflux-original-topic"));
    }

    /// <summary>Preserves unrelated headers without mutating the source message.</summary>
    [Fact]
    public async Task ExistingHeaders_ArePreservedWithoutMutatingSource()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = CreatePublisher(producer);
        var result = CreateResult();
        result.Message.Headers.Add("trace-id", Encoding.UTF8.GetBytes("trace-123"));

        await publisher.PublishFailureAsync(
            result,
            new InvalidOperationException("failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);
        Assert.Equal("trace-123", ReadHeader(published.Message.Headers, "trace-id"));
        Assert.Null(ReadHeader(result.Message.Headers, "x-conflux-retry-attempt"));
        Assert.Equal("trace-123", ReadHeader(result.Message.Headers, "trace-id"));
    }

    /// <summary>Treats invalid attempt metadata as the initial attempt.</summary>
    [Fact]
    public async Task InvalidAttemptHeader_RoutesToFirstRetryTopic()
    {
        using var producer = new FakeKafkaMessageProducer();
        using var publisher = CreatePublisher(producer);

        await publisher.PublishFailureAsync(
            CreateResult(attempt: "invalid"),
            new InvalidOperationException("failure"),
            CancellationToken.None);

        var published = Assert.Single(producer.PublishedMessages);
        Assert.Equal($"{SourceTopic}.retry.1", published.Topic);
        Assert.Equal("1", ReadHeader(published.Message.Headers, "x-conflux-retry-attempt"));
    }

    private static KafkaRetryPublisher CreatePublisher(FakeKafkaMessageProducer producer)
    {
        var options = Options.Create(new KafkaRetryOptions
        {
            BootstrapServers = "unused:9092",
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(10)
        });

        return new KafkaRetryPublisher(options, producer);
    }

    private static ConsumeResult<string, string> CreateResult(
        string topic = SourceTopic,
        string? attempt = null,
        string? originalTopic = null)
    {
        var headers = new Headers();

        if (attempt is not null)
        {
            headers.Add("x-conflux-retry-attempt", Encoding.UTF8.GetBytes(attempt));
        }

        if (originalTopic is not null)
        {
            headers.Add("x-conflux-original-topic", Encoding.UTF8.GetBytes(originalTopic));
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

    private static string? ReadHeader(Headers headers, string name)
    {
        for (var index = headers.Count - 1; index >= 0; index--)
        {
            if (headers[index].Key == name)
            {
                var bytes = headers[index].GetValueBytes();
                return bytes is null ? null : Encoding.UTF8.GetString(bytes);
            }
        }

        return null;
    }
}
