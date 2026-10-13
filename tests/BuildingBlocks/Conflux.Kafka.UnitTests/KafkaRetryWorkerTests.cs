
using System.Text;
using Confluent.Kafka;
using Conflux.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Conflux.Kafka.UnitTests;

/// <summary>
/// Tests retry-worker forwarding, offset handling, and lifecycle behavior.
/// </summary>
public sealed class KafkaRetryWorkerTests
{
    private const string SourceTopic = "conflux.catalog.events";

    /// <summary>
    /// Forwards a retry message to its source topic and commits after publishing.
    /// </summary>
    [Fact]
    public async Task RetryMessage_ForwardsToOriginalTopic_AndCommitsOffset()
    {
        var consumer = new FakeKafkaMessageConsumer([CreateRetryMessage()]);
        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(consumer, producer);

        await worker.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(
                () => consumer.CommittedMessages.Count == 1,
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var published = Assert.Single(producer.PublishedMessages);
        Assert.Equal(SourceTopic, published.Topic);
        Assert.Equal("product-1", published.Message.Key);
        Assert.Equal("{\"sku\":\"TEST-001\"}", published.Message.Value);
        Assert.Null(ReadHeader(
            published.Message.Headers,
            "x-conflux-not-before-unix-ms"));
        Assert.Single(consumer.CommittedMessages);
        Assert.True(consumer.IsClosed);
    }

    /// <summary>
    /// Subscribes to every configured retry topic before processing messages.
    /// </summary>
    [Fact]
    public async Task StartAsync_SubscribesToConfiguredRetryTopics()
    {
        var consumer = new FakeKafkaMessageConsumer([]);
        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(consumer, producer);

        await worker.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(
                () => consumer.SubscribedTopics.Count > 0,
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(
            new[]
            {
                $"{SourceTopic}.retry.1",
                $"{SourceTopic}.retry.2"
            },
            consumer.SubscribedTopics);
    }

    /// <summary>
    /// Routes a retry message without an original-topic header to its fallback DLQ.
    /// </summary>
    [Fact]
    public async Task RetryMessage_WithoutOriginalTopicHeader_RoutesToFallbackDlq()
    {
        var headers = new Headers();
        headers.Add(
            "x-conflux-retry-attempt",
            Encoding.UTF8.GetBytes("1"));

        var message = new ConsumeResult<string, string>
        {
            Topic = $"{SourceTopic}.retry.1",
            Partition = new Partition(0),
            Offset = new Offset(43),
            Message = new Message<string, string>
            {
                Key = "product-2",
                Value = "{\"sku\":\"TEST-002\"}",
                Headers = headers
            }
        };

        var consumer = new FakeKafkaMessageConsumer([message]);
        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(consumer, producer);

        await worker.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(
                () => consumer.CommittedMessages.Count == 1,
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var published = Assert.Single(producer.PublishedMessages);
        Assert.Equal($"{SourceTopic}.retry.1.dlq", published.Topic);
        Assert.Equal("product-2", published.Message.Key);
        Assert.Equal("{\"sku\":\"TEST-002\"}", published.Message.Value);
        Assert.Single(consumer.CommittedMessages);
    }

    /// <summary>
    /// Delays forwarding until the retry message's scheduled time.
    /// </summary>
    [Fact]
    public async Task RetryMessage_WithFutureTimestamp_IsNotForwardedEarly()
    {
        var headers = new Headers();
        headers.Add(
            "x-conflux-original-topic",
            Encoding.UTF8.GetBytes(SourceTopic));
        headers.Add(
            "x-conflux-retry-attempt",
            Encoding.UTF8.GetBytes("1"));
        headers.Add(
            "x-conflux-not-before-unix-ms",
            Encoding.UTF8.GetBytes(
                DateTimeOffset.UtcNow.AddMilliseconds(1200)
                    .ToUnixTimeMilliseconds().ToString()));

        var message = new ConsumeResult<string, string>
        {
            Topic = $"{SourceTopic}.retry.1",
            Partition = new Partition(0),
            Offset = new Offset(44),
            Message = new Message<string, string>
            {
                Key = "product-delayed",
                Value = "{\"sku\":\"TEST-DELAYED\"}",
                Headers = headers
            }
        };

        var consumer = new FakeKafkaMessageConsumer([message]);
        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(consumer, producer);

        await worker.StartAsync(CancellationToken.None);

        try
        {
            await Task.Delay(
                200,
                TestContext.Current.CancellationToken);

            Assert.Empty(producer.PublishedMessages);
            Assert.Empty(consumer.CommittedMessages);

            await WaitUntilAsync(
                () => consumer.CommittedMessages.Count == 1,
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var published = Assert.Single(producer.PublishedMessages);
        Assert.Equal(SourceTopic, published.Topic);
        Assert.Equal("product-delayed", published.Message.Key);
        Assert.Single(consumer.CommittedMessages);
    }

    /// <summary>
    /// Does not commit a retry message when publishing it fails.
    /// </summary>
    [Fact]
    public async Task RetryMessage_WhenPublishingFails_DoesNotCommitOffset()
    {
        var consumer = new FakeKafkaMessageConsumer([CreateRetryMessage()]);
        var producer = new FakeKafkaMessageProducer
        {
            PublishException = new InvalidOperationException(
                "Simulated Kafka publish failure.")
        };

        using var worker = CreateWorker(consumer, producer);

        await worker.StartAsync(CancellationToken.None);

        try
        {
            var execution = worker.ExecuteTask;
            Assert.NotNull(execution);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await execution!);

            Assert.Equal(
                "Simulated Kafka publish failure.",
                exception.Message);
            Assert.Empty(consumer.CommittedMessages);
            Assert.Empty(producer.PublishedMessages);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.True(consumer.IsClosed);
    }

    /// <summary>
    /// Closes and disposes the consumer when the worker is stopped while waiting.
    /// </summary>
    [Fact]
    public async Task StopAsync_WhenConsumerIsWaiting_ClosesConsumerAndCompletesWorker()
    {
        var consumer = new FakeKafkaMessageConsumer([]);
        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(consumer, producer);

        await worker.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(
                () => consumer.SubscribedTopics.Count > 0,
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.True(consumer.IsClosed);
        Assert.True(consumer.IsDisposed);
        Assert.NotNull(worker.ExecuteTask);
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
        Assert.Empty(producer.PublishedMessages);
        Assert.Empty(consumer.CommittedMessages);
    }

    private static KafkaRetryWorker CreateWorker(
        FakeKafkaMessageConsumer consumer,
        FakeKafkaMessageProducer producer)
    {
        var options = Options.Create(new KafkaRetryOptions
        {
            SourceTopics = [SourceTopic],
            MaxAttempts = 3,
            PollInterval = TimeSpan.FromMilliseconds(10)
        });

        return new KafkaRetryWorker(
            options,
            NullLogger<KafkaRetryWorker>.Instance,
            () => consumer,
            () => producer);
    }

    private static ConsumeResult<string, string> CreateRetryMessage()
    {
        var headers = new Headers();
        headers.Add(
            "x-conflux-original-topic",
            Encoding.UTF8.GetBytes(SourceTopic));
        headers.Add(
            "x-conflux-retry-attempt",
            Encoding.UTF8.GetBytes("1"));
        headers.Add(
            "x-conflux-not-before-unix-ms",
            Encoding.UTF8.GetBytes(
                DateTimeOffset.UtcNow.AddSeconds(-1)
                    .ToUnixTimeMilliseconds().ToString()));

        return new ConsumeResult<string, string>
        {
            Topic = $"{SourceTopic}.retry.1",
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
                return bytes is null
                    ? null
                    : Encoding.UTF8.GetString(bytes);
            }
        }

        return null;
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    "The expected worker operation did not complete.");
            }

            await Task.Delay(10);
        }
    }
}
