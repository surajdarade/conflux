using System.Text;
using Confluent.Kafka;
using Conflux.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Conflux.Kafka.UnitTests;

/// <summary>
/// Tests retry-worker forwarding, consumer-specific topic isolation,
/// offset handling, retry scheduling, error handling, and lifecycle behavior.
/// </summary>
public sealed class KafkaRetryWorkerTests
{
    private const string SourceTopic = "conflux.catalog.events";
    private const string OrderSourceTopic = "conflux.order.events";
    private const string FulfillmentRetryIdentity = "fulfillment-order-confirmed";
    private const string OrderRetryIdentity = "order-checkout-saga";

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
    /// Verifies that legacy mode subscribes to the configured retry topics.
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
    /// Verifies that an identity-configured worker subscribes only to
    /// retry topics belonging to its configured consumer identity.
    /// </summary>
    [Fact]
    public async Task StartAsync_WithRetryIdentity_SubscribesToConsumerSpecificRetryTopics()
    {
        var consumer = new FakeKafkaMessageConsumer([]);
        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(
            consumer,
            producer,
            sourceTopic: OrderSourceTopic,
            retryIdentity: FulfillmentRetryIdentity);

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
                $"{OrderSourceTopic}.retry.{FulfillmentRetryIdentity}.1",
                $"{OrderSourceTopic}.retry.{FulfillmentRetryIdentity}.2"
            },
            consumer.SubscribedTopics);
    }

    /// <summary>
    /// Verifies that a retry message with the expected identity is forwarded
    /// to its original source topic and committed after publication.
    /// </summary>
    [Fact]
    public async Task RetryMessage_WithMatchingIdentity_ForwardsToOriginalTopic()
    {
        var consumer = new FakeKafkaMessageConsumer(
        [
            CreateRetryMessage(
                sourceTopic: OrderSourceTopic,
                retryIdentity: FulfillmentRetryIdentity)
        ]);

        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(
            consumer,
            producer,
            sourceTopic: OrderSourceTopic,
            retryIdentity: FulfillmentRetryIdentity);

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

        Assert.Equal(OrderSourceTopic, published.Topic);
        Assert.Equal("product-1", published.Message.Key);
        Assert.Equal(
            FulfillmentRetryIdentity,
            ReadHeader(
                published.Message.Headers,
                "x-conflux-retry-identity"));
        Assert.Null(ReadHeader(
            published.Message.Headers,
            "x-conflux-not-before-unix-ms"));
        Assert.Single(consumer.CommittedMessages);
    }

    /// <summary>
    /// Verifies that a retry message without an identity header is sent to
    /// the configured consumer's DLQ instead of being replayed.
    /// </summary>
    [Fact]
    public async Task RetryMessage_WithoutIdentityHeader_RoutesToConsumerSpecificDlq()
    {
        var consumer = new FakeKafkaMessageConsumer(
        [
            CreateRetryMessage(
                sourceTopic: OrderSourceTopic,
                retryIdentity: FulfillmentRetryIdentity,
                includeRetryIdentityHeader: false)
        ]);

        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(
            consumer,
            producer,
            sourceTopic: OrderSourceTopic,
            retryIdentity: FulfillmentRetryIdentity);

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

        Assert.Equal(
            $"{OrderSourceTopic}.dlq.{FulfillmentRetryIdentity}",
            published.Topic);
        Assert.NotEqual(OrderSourceTopic, published.Topic);
        Assert.Single(consumer.CommittedMessages);
    }

    /// <summary>
    /// Verifies that a retry message belonging to a different consumer identity
    /// is sent to this worker's DLQ instead of being replayed.
    /// </summary>
    [Fact]
    public async Task RetryMessage_WithMismatchedIdentity_RoutesToConsumerSpecificDlq()
    {
        var consumer = new FakeKafkaMessageConsumer(
        [
            CreateRetryMessage(
                sourceTopic: OrderSourceTopic,
                retryIdentity: OrderRetryIdentity)
        ]);

        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(
            consumer,
            producer,
            sourceTopic: OrderSourceTopic,
            retryIdentity: FulfillmentRetryIdentity);

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

        Assert.Equal(
            $"{OrderSourceTopic}.dlq.{FulfillmentRetryIdentity}",
            published.Topic);
        Assert.Equal(
            OrderRetryIdentity,
            ReadHeader(
                published.Message.Headers,
                "x-conflux-retry-identity"));
        Assert.NotEqual(OrderSourceTopic, published.Topic);
        Assert.Single(consumer.CommittedMessages);
    }

    /// <summary>
    /// Verifies that an unconfigured original topic is quarantined using the
    /// consumed retry topic rather than a destination derived from that header.
    /// </summary>
    [Fact]
    public async Task RetryMessage_WithUnconfiguredSourceTopic_UsesFallbackDlq()
    {
        var consumer = new FakeKafkaMessageConsumer(
        [
            CreateRetryMessage(
                sourceTopic: "conflux.unconfigured.events",
                retryIdentity: FulfillmentRetryIdentity)
        ]);
        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(
            consumer,
            producer,
            sourceTopic: OrderSourceTopic,
            retryIdentity: FulfillmentRetryIdentity);

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
        Assert.Equal(
            $"conflux.unconfigured.events.retry.{FulfillmentRetryIdentity}.1.dlq.{FulfillmentRetryIdentity}",
            published.Topic);
        Assert.Single(consumer.CommittedMessages);
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
    /// Verifies that a retry message is not forwarded before its scheduled time.
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
    /// Verifies that a retry message is not committed when publishing fails.
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
    /// Verifies that stopping the worker closes and disposes its consumer
    /// and completes execution when no retry messages are available.
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

    /// <summary>
    /// Creates a retry worker with injectable fake Kafka clients.
    /// </summary>
    /// <param name="consumer">Fake consumer used by the worker.</param>
    /// <param name="producer">Fake producer used by the worker.</param>
    /// <param name="sourceTopic">Configured source topic.</param>
    /// <param name="retryIdentity">
    /// Optional consumer-specific retry identity. Empty selects legacy topic naming.
    /// </param>
    /// <returns>The configured retry worker.</returns>
    private static KafkaRetryWorker CreateWorker(
        FakeKafkaMessageConsumer consumer,
        FakeKafkaMessageProducer producer,
        string sourceTopic = SourceTopic,
        string retryIdentity = "")
    {
        var options = Options.Create(new KafkaRetryOptions
        {
            SourceTopics = [sourceTopic],
            RetryIdentity = retryIdentity,
            MaxAttempts = 3,
            PollInterval = TimeSpan.FromMilliseconds(10)
        });

        return new KafkaRetryWorker(
            options,
            NullLogger<KafkaRetryWorker>.Instance,
            () => consumer,
            () => producer);
    }

    /// <summary>
    /// Creates a retry message with scheduling metadata and optional consumer identity.
    /// </summary>
    /// <param name="sourceTopic">Original source topic.</param>
    /// <param name="retryIdentity">
    /// Optional retry identity. When provided, the retry topic uses identity-specific naming.
    /// </param>
    /// <param name="includeRetryIdentityHeader">
    /// Whether to include the retry identity header in the message.
    /// </param>
    /// <returns>A synthetic consumed Kafka retry message.</returns>
    private static ConsumeResult<string, string> CreateRetryMessage(
        string sourceTopic = SourceTopic,
        string? retryIdentity = null,
        bool includeRetryIdentityHeader = true)
    {
        var headers = new Headers();

        headers.Add(
            "x-conflux-original-topic",
            Encoding.UTF8.GetBytes(sourceTopic));
        headers.Add(
            "x-conflux-retry-attempt",
            Encoding.UTF8.GetBytes("1"));
        headers.Add(
            "x-conflux-not-before-unix-ms",
            Encoding.UTF8.GetBytes(
                DateTimeOffset.UtcNow.AddSeconds(-1)
                    .ToUnixTimeMilliseconds().ToString()));

        if (!string.IsNullOrWhiteSpace(retryIdentity)
            && includeRetryIdentityHeader)
        {
            headers.Add(
                "x-conflux-retry-identity",
                Encoding.UTF8.GetBytes(retryIdentity));
        }

        var retryTopic = string.IsNullOrWhiteSpace(retryIdentity)
            ? $"{sourceTopic}.retry.1"
            : $"{sourceTopic}.retry.{retryIdentity}.1";

        return new ConsumeResult<string, string>
        {
            Topic = retryTopic,
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
    /// Reads the last occurrence of a Kafka header and decodes its value as UTF-8.
    /// </summary>
    /// <param name="headers">Headers to inspect.</param>
    /// <param name="name">Header name to locate.</param>
    /// <returns>The decoded value, or null if the header is absent.</returns>
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

    /// <summary>
    /// Waits for a condition to become true or throws when the timeout expires.
    /// </summary>
    /// <param name="condition">Condition to evaluate.</param>
    /// <param name="timeout">Maximum time to wait.</param>
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