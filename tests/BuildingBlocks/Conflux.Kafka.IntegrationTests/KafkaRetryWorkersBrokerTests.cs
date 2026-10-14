
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Conflux.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Conflux.Kafka.IntegrationTests;

/// <summary>
/// Exercises the production Kafka retry and DLQ recovery workers against a real broker.
/// Each test creates uniquely named topics and never uses business-event topics.
/// </summary>
public sealed class KafkaRetryWorkersBrokerTests
{
    private const string OriginalTopicHeader = "x-conflux-original-topic";
    private const string RetryIdentityHeader = "x-conflux-retry-identity";
    private const string RetryAttemptHeader = "x-conflux-retry-attempt";
    private const string NotBeforeHeader = "x-conflux-not-before-unix-ms";
    private const string RecoveredHeader = "x-conflux-recovered";

    /// <summary>
    /// Verifies identity-specific retry messages are forwarded to their configured source topic.
    /// </summary>
    [KafkaBrokerFact]
    public async Task RetryWorker_ForwardsIdentitySpecificRetryToConfiguredSource()
    {
        var fixture = await BrokerFixture.CreateAsync();
        KafkaRetryWorker? worker = null;
        IConsumer<string, string>? sourceConsumer = null;

        try
        {
            sourceConsumer = fixture.CreateConsumer("source-observer");
            sourceConsumer.Subscribe(fixture.SourceTopic);

            worker = new KafkaRetryWorker(
                Options.Create(fixture.Options),
                NullLogger<KafkaRetryWorker>.Instance);

            await worker.StartAsync(CancellationToken.None);

            using var publisher = new KafkaRetryPublisher(Options.Create(fixture.Options));

            await publisher.PublishFailureAsync(
                CreateConsumedResult(fixture.SourceTopic),
                new InvalidOperationException("integration-test-transient-failure"),
                CancellationToken.None);

            var replayed = ConsumeRequired(sourceConsumer, TimeSpan.FromSeconds(15));

            Assert.Equal(fixture.SourceTopic, replayed.Topic);
            Assert.Equal("integration-key", replayed.Message.Key);
            Assert.Equal("integration-payload", replayed.Message.Value);
            Assert.Equal(
                fixture.Identity,
                ReadHeader(replayed.Message.Headers, RetryIdentityHeader));
            Assert.Null(ReadHeader(replayed.Message.Headers, NotBeforeHeader));
        }
        finally
        {
            if (worker is not null)
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await worker.StopAsync(stopTimeout.Token);
                worker.Dispose();
            }

            sourceConsumer?.Close();
            sourceConsumer?.Dispose();

            await fixture.DisposeAsync();
        }
    }

    /// <summary>
    /// Verifies an identity-specific dead-letter message is recovered to the source topic.
    /// </summary>
    [KafkaBrokerFact]
    public async Task DlqRecoveryWorker_RecoversIdentitySpecificDeadLetterOnce()
    {
        var fixture = await BrokerFixture.CreateAsync();
        KafkaDlqRecoveryWorker? worker = null;
        IConsumer<string, string>? sourceConsumer = null;

        try
        {
            sourceConsumer = fixture.CreateConsumer("source-observer");
            sourceConsumer.Subscribe(fixture.SourceTopic);

            worker = new KafkaDlqRecoveryWorker(
                Options.Create(fixture.Options),
                NullLogger<KafkaDlqRecoveryWorker>.Instance);

            await worker.StartAsync(CancellationToken.None);

            using var publisher = new KafkaRetryPublisher(Options.Create(fixture.Options));

            await publisher.PublishFailureAsync(
                CreateConsumedResult(fixture.SourceTopic, attempt: "2"),
                new InvalidOperationException("integration-test-terminal-failure"),
                CancellationToken.None);

            var recovered = ConsumeRequired(sourceConsumer, TimeSpan.FromSeconds(15));

            Assert.Equal(fixture.SourceTopic, recovered.Topic);
            Assert.Equal("integration-key", recovered.Message.Key);
            Assert.Equal("integration-payload", recovered.Message.Value);
            Assert.Equal(
                fixture.Identity,
                ReadHeader(recovered.Message.Headers, RetryIdentityHeader));
            Assert.Equal("true", ReadHeader(recovered.Message.Headers, RecoveredHeader));
            Assert.Equal("0", ReadHeader(recovered.Message.Headers, RetryAttemptHeader));
            Assert.NotNull(ReadHeader(recovered.Message.Headers, "x-conflux-recovered-at"));
            Assert.Null(ReadHeader(recovered.Message.Headers, NotBeforeHeader));
        }
        finally
        {
            if (worker is not null)
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await worker.StopAsync(stopTimeout.Token);
                worker.Dispose();
            }

            sourceConsumer?.Close();
            sourceConsumer?.Dispose();

            await fixture.DisposeAsync();
        }
    }

    /// <summary>
    /// Verifies a retry message without an identity is quarantined instead of replayed.
    /// </summary>
    [KafkaBrokerFact]
    public async Task RetryWorker_QuarantinesMessageWithMissingIdentityInsteadOfReplaying()
    {
        var fixture = await BrokerFixture.CreateAsync();
        KafkaRetryWorker? worker = null;
        IConsumer<string, string>? dlqConsumer = null;

        try
        {
            dlqConsumer = fixture.CreateConsumer("dlq-observer");
            dlqConsumer.Subscribe(fixture.DlqTopic);

            worker = new KafkaRetryWorker(
                Options.Create(fixture.Options),
                NullLogger<KafkaRetryWorker>.Instance);

            await worker.StartAsync(CancellationToken.None);

            using var producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = fixture.BootstrapServers,
                EnableIdempotence = true,
                Acks = Acks.All
            }).Build();

            var headers = new Headers();
            headers.Add(
                OriginalTopicHeader,
                Encoding.UTF8.GetBytes(fixture.SourceTopic));
            headers.Add(
                RetryAttemptHeader,
                Encoding.UTF8.GetBytes("1"));

            // Deliberately omit x-conflux-retry-identity.

            await producer.ProduceAsync(
                fixture.RetryTopic1,
                new Message<string, string>
                {
                    Key = "quarantine-key",
                    Value = "missing-identity-payload",
                    Headers = headers
                });

            var quarantined = ConsumeRequired(dlqConsumer, TimeSpan.FromSeconds(15));

            Assert.Equal(fixture.DlqTopic, quarantined.Topic);
            Assert.Equal("quarantine-key", quarantined.Message.Key);
            Assert.Equal("missing-identity-payload", quarantined.Message.Value);
            Assert.Null(ReadHeader(quarantined.Message.Headers, RetryIdentityHeader));
        }
        finally
        {
            if (worker is not null)
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await worker.StopAsync(stopTimeout.Token);
                worker.Dispose();
            }

            dlqConsumer?.Close();
            dlqConsumer?.Dispose();

            await fixture.DisposeAsync();
        }
    }

    private static ConsumeResult<string, string> CreateConsumedResult(
        string sourceTopic,
        string? attempt = null)
    {
        var headers = new Headers();

        if (attempt is not null)
        {
            headers.Add(RetryAttemptHeader, Encoding.UTF8.GetBytes(attempt));
        }

        return new ConsumeResult<string, string>
        {
            Topic = sourceTopic,
            Partition = new Partition(0),
            Offset = new Offset(0),
            Message = new Message<string, string>
            {
                Key = "integration-key",
                Value = "integration-payload",
                Headers = headers,
                Timestamp = new Timestamp(DateTime.UtcNow)
            }
        };
    }

    private static ConsumeResult<string, string> ConsumeRequired(
        IConsumer<string, string> consumer,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);

        try
        {
            return consumer.Consume(cancellation.Token);
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException(
                $"No Kafka message arrived within {timeout}.",
                exception);
        }
    }

    private static string? ReadHeader(Headers headers, string name)
    {
        for (var index = headers.Count - 1; index >= 0; index--)
        {
            if (string.Equals(headers[index].Key, name, StringComparison.Ordinal))
            {
                var value = headers[index].GetValueBytes();

                return value is { Length: > 0 }
                    ? Encoding.UTF8.GetString(value)
                    : string.Empty;
            }
        }

        return null;
    }

    private sealed class BrokerFixture : IAsyncDisposable
    {
        private readonly IAdminClient _adminClient;

        private BrokerFixture(string bootstrapServers, string suffix)
        {
            BootstrapServers = bootstrapServers;
            Identity = $"integration-{suffix}";
            SourceTopic = $"conflux.integration-{suffix}.source";
            RetryTopic1 = KafkaRetryOptions.GetRetryTopic(SourceTopic, Identity, 1);
            RetryTopic2 = KafkaRetryOptions.GetRetryTopic(SourceTopic, Identity, 2);
            DlqTopic = KafkaRetryOptions.GetDlqTopic(SourceTopic, Identity);
            PermanentDlqTopic = DlqTopic + ".permanent";

            _adminClient = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = BootstrapServers
            }).Build();

            Options = new KafkaRetryOptions
            {
                BootstrapServers = BootstrapServers,
                SourceTopics = [SourceTopic],
                RetryIdentity = Identity,
                ConsumerGroup = $"conflux-kafka-integration-{suffix}",
                MaxAttempts = 3,
                BaseDelay = TimeSpan.FromMilliseconds(25),
                PollInterval = TimeSpan.FromMilliseconds(50),
                AutomaticDlqRecovery = true,
                DlqRecoveryDelay = TimeSpan.FromMilliseconds(25)
            };
        }

        public string BootstrapServers { get; }

        public string Identity { get; }

        public string SourceTopic { get; }

        public string RetryTopic1 { get; }

        public string RetryTopic2 { get; }

        public string DlqTopic { get; }

        public string PermanentDlqTopic { get; }

        public KafkaRetryOptions Options { get; }

        public static async Task<BrokerFixture> CreateAsync()
        {
            var bootstrapServers = Environment.GetEnvironmentVariable(
                "CONFLUX_KAFKA_BOOTSTRAP_SERVERS");

            if (string.IsNullOrWhiteSpace(bootstrapServers))
            {
                throw new InvalidOperationException(
                    "CONFLUX_KAFKA_BOOTSTRAP_SERVERS is required for broker integration tests.");
            }

            var fixture = new BrokerFixture(
                bootstrapServers,
                Guid.NewGuid().ToString("N"));

            var topics = new[]
            {
                fixture.SourceTopic,
                fixture.RetryTopic1,
                fixture.RetryTopic2,
                fixture.DlqTopic,
                fixture.PermanentDlqTopic
            };

            try
            {
                await fixture._adminClient.CreateTopicsAsync(
                    topics.Select(topic => new TopicSpecification
                    {
                        Name = topic,
                        NumPartitions = 1,
                        ReplicationFactor = 1
                    }));
            }
            catch (CreateTopicsException exception) when (
                exception.Results.All(result =>
                    result.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                // A GUID collision is exceptionally unlikely; accepting existing
                // topics keeps the fixture resilient to a partially repeated run.
            }

            // Topic creation can complete before the broker's metadata is visible
            // to consumers. Wait for every test topic to become usable.
            await WaitForTopicsAsync(fixture._adminClient, topics);

            return fixture;
        }

        private static async Task WaitForTopicsAsync(
            IAdminClient adminClient,
            IReadOnlyCollection<string> expectedTopics)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            var expected = new HashSet<string>(expectedTopics, StringComparer.Ordinal);
            Exception? lastError = null;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(2));

                    var readyTopics = metadata.Topics
                        .Where(topic =>
                            topic.Error.Code == ErrorCode.NoError &&
                            topic.Partitions.Count > 0 &&
                            topic.Partitions.All(
                                partition => partition.Error.Code == ErrorCode.NoError))
                        .Select(topic => topic.Topic)
                        .ToHashSet(StringComparer.Ordinal);

                    if (expected.IsSubsetOf(readyTopics))
                    {
                        return;
                    }
                }
                catch (KafkaException exception)
                {
                    lastError = exception;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            throw new TimeoutException(
                $"Kafka topics were not ready within 15 seconds: " +
                $"{string.Join(", ", expected)}.",
                lastError);
        }

        public IConsumer<string, string> CreateConsumer(string suffix)
        {
            return new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = BootstrapServers,
                GroupId = $"{Options.ConsumerGroup}-{suffix}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
                AllowAutoCreateTopics = false
            }).Build();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _adminClient.DeleteTopicsAsync(
                [
                    SourceTopic,
                    RetryTopic1,
                    RetryTopic2,
                    DlqTopic,
                    PermanentDlqTopic
                ]);
            }
            catch (DeleteTopicsException)
            {
                // Broker-side topic deletion can be disabled or asynchronous.
                // Unique names prevent these leftovers from affecting future runs.
            }
            finally
            {
                _adminClient.Dispose();
            }
        }
    }
}
