using System.Text;
using Confluent.Kafka;
using Conflux.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Conflux.Kafka.UnitTests;

/// <summary>
/// Tests automatic DLQ recovery, identity isolation, offset handling,
/// and lifecycle behavior.
/// </summary>
public sealed class KafkaDlqRecoveryWorkerTests
{
    private const string SourceTopic = "conflux.catalog.events";
    private const string DlqTopic = SourceTopic + ".dlq";
    private const string PermanentDlqTopic = DlqTopic + ".permanent";

    private const string RetryIdentity = "catalog-projection";
    private const string IdentityDlqTopic = SourceTopic + ".dlq." + RetryIdentity;
    private const string IdentityPermanentDlqTopic =
        IdentityDlqTopic + ".permanent";

    private const string RetryIdentityHeader = "x-conflux-retry-identity";

    /// <summary>
    /// Recovers a DLQ message to its original topic and commits its offset.
    /// </summary>
    [Fact]
    public async Task DlqMessage_WithOriginalTopic_IsRecoveredAndCommitted()
    {
        var message = CreateDlqMessage();
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

        Assert.Equal(SourceTopic, published.Topic);
        Assert.Equal("product-1", published.Message.Key);
        Assert.Equal("{\"sku\":\"TEST-001\"}", published.Message.Value);

        Assert.Equal(
            "0",
            ReadHeader(published.Message.Headers, "x-conflux-retry-attempt"));

        Assert.Equal(
            "true",
            ReadHeader(published.Message.Headers, "x-conflux-recovered"));

        Assert.NotNull(
            ReadHeader(published.Message.Headers, "x-conflux-recovered-at"));

        Assert.Null(
            ReadHeader(published.Message.Headers, "x-conflux-not-before-unix-ms"));

        Assert.Single(consumer.CommittedMessages);
        Assert.True(consumer.IsClosed);
        Assert.True(consumer.IsDisposed);
    }

    /// <summary>
    /// Routes a previously recovered message to the permanent DLQ.
    /// </summary>
    [Fact]
    public async Task DlqMessage_AlreadyRecovered_IsRoutedToPermanentDlq()
    {
        var message = CreateDlqMessage(recovered: true);
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

        Assert.Equal(PermanentDlqTopic, published.Topic);
        Assert.Equal("product-1", published.Message.Key);
        Assert.Equal("{\"sku\":\"TEST-001\"}", published.Message.Value);
        Assert.Single(consumer.CommittedMessages);
    }

    /// <summary>
    /// Routes a message without original-topic metadata to the permanent DLQ.
    /// </summary>
    [Fact]
    public async Task DlqMessage_WithoutOriginalTopic_IsRoutedToPermanentDlq()
    {
        var message = CreateDlqMessage(includeOriginalTopic: false);
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

        Assert.Equal(PermanentDlqTopic, published.Topic);
        Assert.Equal("product-1", published.Message.Key);
        Assert.Single(consumer.CommittedMessages);
    }

    /// <summary>
    /// Waits for the configured recovery delay before publishing.
    /// </summary>
    [Fact]
    public async Task DlqMessage_WithFutureRecoveryDelay_IsNotPublishedEarly()
    {
        var consumer = new FakeKafkaMessageConsumer([CreateDlqMessage()]);
        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(
            consumer,
            producer,
            recoveryDelay: TimeSpan.FromMilliseconds(1000));

        await worker.StartAsync(CancellationToken.None);

        try
        {
            await Task.Delay(
                150,
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

        Assert.Single(producer.PublishedMessages);
        Assert.Single(consumer.CommittedMessages);
    }

    /// <summary>
    /// Does not commit the offset when recovery publication fails.
    /// </summary>
    [Fact]
    public async Task DlqMessage_WhenPublishingFails_DoesNotCommitOffset()
    {
        var consumer = new FakeKafkaMessageConsumer([CreateDlqMessage()]);
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
        Assert.True(consumer.IsDisposed);
    }

    /// <summary>
    /// Does not create Kafka clients when automatic recovery is disabled.
    /// </summary>
    [Fact]
    public async Task StartAsync_WhenAutomaticRecoveryIsDisabled_DoesNotCreateClients()
    {
        using var worker = new KafkaDlqRecoveryWorker(
            Options.Create(new KafkaRetryOptions
            {
                SourceTopics = [SourceTopic],
                AutomaticDlqRecovery = false
            }),
            NullLogger<KafkaDlqRecoveryWorker>.Instance,
            () => throw new InvalidOperationException(
                "Consumer factory should not be called."),
            () => throw new InvalidOperationException(
                "Producer factory should not be called."));

        await worker.StartAsync(CancellationToken.None);

        var execution = worker.ExecuteTask;
        Assert.NotNull(execution);
        await execution;

        Assert.True(execution.IsCompletedSuccessfully);
    }

    /// <summary>
    /// Does not create Kafka clients when no source topics are configured.
    /// </summary>
    [Fact]
    public async Task StartAsync_WhenSourceTopicsAreEmpty_DoesNotCreateClients()
    {
        using var worker = new KafkaDlqRecoveryWorker(
            Options.Create(new KafkaRetryOptions
            {
                SourceTopics = [],
                AutomaticDlqRecovery = true
            }),
            NullLogger<KafkaDlqRecoveryWorker>.Instance,
            () => throw new InvalidOperationException(
                "Consumer factory should not be called."),
            () => throw new InvalidOperationException(
                "Producer factory should not be called."));

        await worker.StartAsync(CancellationToken.None);

        var execution = worker.ExecuteTask;
        Assert.NotNull(execution);
        await execution;

        Assert.True(execution.IsCompletedSuccessfully);
    }

    /// <summary>
    /// Closes and disposes the consumer during graceful shutdown.
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
    /// Subscribes to identity-specific DLQ topics when a retry identity is set.
    /// </summary>
    [Fact]
    public async Task StartAsync_WithRetryIdentity_SubscribesToIdentitySpecificDlqTopics()
    {
        var consumer = new FakeKafkaMessageConsumer([]);
        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(
            consumer,
            producer,
            retryIdentity: RetryIdentity);

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

        Assert.Equal([IdentityDlqTopic], consumer.SubscribedTopics);
    }

    /// <summary>
    /// Recovers a message with the matching identity and preserves that identity.
    /// </summary>
    [Fact]
    public async Task DlqMessage_WithMatchingIdentity_IsRecoveredAndIdentityPreserved()
    {
        var message = CreateDlqMessage(retryIdentity: RetryIdentity);
        var consumer = new FakeKafkaMessageConsumer([message]);
        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(
            consumer,
            producer,
            retryIdentity: RetryIdentity);

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
        Assert.Equal(
            RetryIdentity,
            ReadHeader(published.Message.Headers, RetryIdentityHeader));
        Assert.Equal(
            "true",
            ReadHeader(published.Message.Headers, "x-conflux-recovered"));
        Assert.Single(consumer.CommittedMessages);
    }

    /// <summary>
    /// Routes a message without an identity to the identity-specific permanent DLQ.
    /// </summary>
    [Fact]
    public async Task DlqMessage_WithoutIdentity_IsRoutedToPermanentDlqWithoutReplay()
    {
        var message = CreateDlqMessage(
            includeIdentitySpecificTopic: true);

        var consumer = new FakeKafkaMessageConsumer([message]);
        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(
            consumer,
            producer,
            retryIdentity: RetryIdentity);

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

        Assert.Equal(IdentityPermanentDlqTopic, published.Topic);
        Assert.Single(consumer.CommittedMessages);
    }

    /// <summary>
    /// Routes a message with a different identity to the permanent DLQ.
    /// </summary>
    [Fact]
    public async Task DlqMessage_WithMismatchedIdentity_IsRoutedToPermanentDlqWithoutReplay()
    {
        var message = CreateDlqMessage(
            retryIdentity: "order-checkout-saga",
            includeIdentitySpecificTopic: true);

        var consumer = new FakeKafkaMessageConsumer([message]);
        var producer = new FakeKafkaMessageProducer();

        using var worker = CreateWorker(
            consumer,
            producer,
            retryIdentity: RetryIdentity);

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

        Assert.Equal(IdentityPermanentDlqTopic, published.Topic);
        Assert.Single(consumer.CommittedMessages);
    }

    /// <summary>
    /// Rejects a message whose original topic is not configured.
    /// </summary>
    [Fact]
    public async Task DlqMessage_WithUnconfiguredOriginalTopic_IsRoutedToPermanentDlq()
    {
        var message = CreateDlqMessage(
            originalTopic: "conflux.unconfigured.events");

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

        Assert.Equal(PermanentDlqTopic, published.Topic);
        Assert.Single(consumer.CommittedMessages);
    }

    private static KafkaDlqRecoveryWorker CreateWorker(
        FakeKafkaMessageConsumer consumer,
        FakeKafkaMessageProducer producer,
        TimeSpan? recoveryDelay = null,
        string retryIdentity = "")
    {
        var options = Options.Create(new KafkaRetryOptions
        {
            SourceTopics = [SourceTopic],
            RetryIdentity = retryIdentity,
            MaxAttempts = 3,
            AutomaticDlqRecovery = true,
            DlqRecoveryDelay = recoveryDelay ?? TimeSpan.Zero
        });

        return new KafkaDlqRecoveryWorker(
            options,
            NullLogger<KafkaDlqRecoveryWorker>.Instance,
            () => consumer,
            () => producer);
    }

    private static ConsumeResult<string, string> CreateDlqMessage(
        bool recovered = false,
        bool includeOriginalTopic = true,
        string? retryIdentity = null,
        string? originalTopic = null,
        bool includeIdentitySpecificTopic = false)
    {
        var headers = new Headers();

        if (includeOriginalTopic)
        {
            headers.Add(
                "x-conflux-original-topic",
                Encoding.UTF8.GetBytes(originalTopic ?? SourceTopic));
        }

        headers.Add(
            "x-conflux-retry-attempt",
            Encoding.UTF8.GetBytes("3"));

        headers.Add(
            "x-conflux-not-before-unix-ms",
            Encoding.UTF8.GetBytes(
                DateTimeOffset.UtcNow.AddMinutes(-1)
                    .ToUnixTimeMilliseconds().ToString()));

        if (retryIdentity is not null)
        {
            headers.Add(
                RetryIdentityHeader,
                Encoding.UTF8.GetBytes(retryIdentity));
        }

        if (recovered)
        {
            headers.Add(
                "x-conflux-recovered",
                Encoding.UTF8.GetBytes("true"));
        }

        var topic = includeIdentitySpecificTopic || retryIdentity is not null
            ? IdentityDlqTopic
            : DlqTopic;

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

            await Task.Delay(
                10,
                TestContext.Current.CancellationToken);
        }
    }
}