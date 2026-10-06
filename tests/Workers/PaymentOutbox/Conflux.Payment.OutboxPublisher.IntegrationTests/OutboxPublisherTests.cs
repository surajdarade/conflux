using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Conflux.Outbox;
using Conflux.Payment.OutboxPublisher;
using Conflux.Payment.OutboxPublisher.Configuration;
using Conflux.Payment.OutboxPublisher.IntegrationTests.Infrastructure;
using Conflux.Payment.OutboxPublisher.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Conflux.Payment.OutboxPublisher.IntegrationTests;

/// <summary>
/// Verifies the end-to-end behavior of the Payment Outbox Publisher.
/// </summary>
[Collection("Payment Outbox Publisher integration tests")]
public sealed class OutboxPublisherTests {
    private const string Topic =
        "conflux.payment.events";

    private readonly OutboxPublisherTestFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="OutboxPublisherTests"/> class.
    /// </summary>
    /// <param name="fixture">
    /// The shared integration-test infrastructure.
    /// </param>
    public OutboxPublisherTests(
        OutboxPublisherTestFixture fixture) {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies that an unpublished Outbox message is published to Kafka
    /// and marked as published after successful delivery.
    /// </summary>
    [Fact]
    public async Task PublishesOutboxMessageToKafka_AndMarksMessagePublished() {
        var eventId =
            Guid.NewGuid();

        var correlationId =
            Guid.NewGuid();

        var occurredAt =
            DateTimeOffset.UtcNow;

        const string eventType =
            "payment.authorized.v1";

        const string payload =
            """
            {
              "eventId": "00000000-0000-0000-0000-000000000001",
              "occurredAt": "2026-01-01T00:00:00+00:00",
              "correlationId": "00000000-0000-0000-0000-000000000002",
              "causationId": null,
              "paymentId": "00000000-0000-0000-0000-000000000003",
              "orderId": "00000000-0000-0000-0000-000000000004",
              "customerId": "00000000-0000-0000-0000-000000000005",
              "amount": 1499.99,
              "currency": "INR"
            }
            """;

        var outboxMessage =
            new OutboxMessage(
                eventId,
                occurredAt,
                eventType,
                payload,
                correlationId,
                null);

        await using (
            var dbContext =
                _fixture.CreateDbContext()) {
            dbContext.OutboxMessages.Add(
                outboxMessage);

            await dbContext.SaveChangesAsync(
                TestContext.Current.CancellationToken);
        }

        await CreateTopicAsync();

        using var serviceProvider =
            CreatePublisherServiceProvider(
                new ConfluentKafkaEventPublisher(
                    _fixture.KafkaBootstrapAddress));

        var worker =
            serviceProvider
                .GetRequiredService<OutboxPublisherWorker>();

        await worker.StartAsync(
            TestContext.Current.CancellationToken);

        try {
            var publishedMessage =
                await ConsumeMessageAsync(
                    eventId);

            publishedMessage.Should().NotBeNull();

            publishedMessage!.Message.Key
                .Should()
                .Be(eventId.ToString());

            publishedMessage.Message.Value
                .Should()
                .Be(payload);

            GetHeader(
                    publishedMessage.Message.Headers,
                    "event-type")
                .Should()
                .Be(eventType);

            GetHeader(
                    publishedMessage.Message.Headers,
                    "event-id")
                .Should()
                .Be(eventId.ToString());

            GetHeader(
                    publishedMessage.Message.Headers,
                    "correlation-id")
                .Should()
                .Be(correlationId.ToString());

            await WaitForOutboxMessageToBePublishedAsync(
                eventId);

            await using var verificationContext =
                _fixture.CreateDbContext();

            var persistedMessage =
                await verificationContext.OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(
                        message =>
                            message.Id == eventId,
                        TestContext.Current.CancellationToken);

            persistedMessage.PublishedAt
                .Should()
                .NotBeNull();

            persistedMessage.AttemptCount
                .Should()
                .Be(1);

            persistedMessage.FirstAttemptedAt
                .Should()
                .NotBeNull();

            persistedMessage.LastError
                .Should()
                .BeNull();
        }
        finally {
            await worker.StopAsync(
                TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Verifies that a failed publication is persisted as a failed attempt
    /// and the same Outbox message is retried successfully.
    /// </summary>
    [Fact]
    public async Task FailedPublication_IsRetried_AndEventuallyMarkedPublished() {
        var eventId =
            Guid.NewGuid();

        var correlationId =
            Guid.NewGuid();

        var outboxMessage =
            new OutboxMessage(
                eventId,
                DateTimeOffset.UtcNow,
                "payment.authorized.v1",
                """{"paymentId":"retry-test"}""",
                correlationId,
                null);

        await using (
            var dbContext =
                _fixture.CreateDbContext()) {
            dbContext.OutboxMessages.Add(
                outboxMessage);

            await dbContext.SaveChangesAsync(
                TestContext.Current.CancellationToken);
        }

        var publisher =
            new FailOnceKafkaEventPublisher();

        using var serviceProvider =
            CreatePublisherServiceProvider(
                publisher);

        var worker =
            serviceProvider
                .GetRequiredService<OutboxPublisherWorker>();

        await worker.StartAsync(
            TestContext.Current.CancellationToken);

        try {
            var publishedMessage =
                await WaitForOutboxStateAsync(
                    eventId,
                    message =>
                        message.PublishedAt.HasValue &&
                        message.AttemptCount == 2);

            publishedMessage.PublishedAt
                .Should()
                .NotBeNull();

            publishedMessage.AttemptCount
                .Should()
                .Be(2);

            publishedMessage.FirstAttemptedAt
                .Should()
                .NotBeNull();

            publishedMessage.LastError
                .Should()
                .BeNull();

            publisher.AttemptCount
                .Should()
                .Be(2);
        }
        finally {
            await worker.StopAsync(
                TestContext.Current.CancellationToken);
        }
    }

    private ServiceProvider CreatePublisherServiceProvider(
        IKafkaEventPublisher kafkaEventPublisher) {
        var services =
            new ServiceCollection();

        services.AddLogging(
            builder =>
                builder.SetMinimumLevel(
                    LogLevel.Warning));

        services.AddSingleton(
            kafkaEventPublisher);

        services.AddSingleton(
            Options.Create(
                new KafkaOptions
                {
                    BootstrapServers =
                        _fixture.KafkaBootstrapAddress,

                    PaymentEventsTopic =
                        Topic,

                    BatchSize = 100,

                    PollInterval =
                        TimeSpan.FromMilliseconds(100)
                }));

        services.AddDbContext<
            Conflux.Payment.Infrastructure.PaymentDbContext>(
            options =>
                options.UseNpgsql(
                    _fixture.PostgresConnectionString));

        services.AddSingleton<
            OutboxPublisherWorker>();

        return services.BuildServiceProvider();
    }

    private async Task CreateTopicAsync() {
        var configuration =
            new AdminClientConfig
            {
                BootstrapServers =
                    _fixture.KafkaBootstrapAddress
            };

        using var adminClient =
            new AdminClientBuilder(configuration)
                .Build();

        try {
            await adminClient.CreateTopicsAsync(
                [
                    new TopicSpecification
                    {
                        Name = Topic,
                        NumPartitions = 1,
                        ReplicationFactor = 1
                    }
                ]);
        }
        catch (CreateTopicsException exception)
            when (
                exception.Results.Any(
                    result =>
                        result.Error.Code ==
                        ErrorCode.TopicAlreadyExists)) {
            // The topic already exists.
        }
    }

    private async Task<ConsumeResult<string, string>?>
        ConsumeMessageAsync(
            Guid expectedEventId) {
        var configuration =
            new ConsumerConfig
            {
                BootstrapServers =
                    _fixture.KafkaBootstrapAddress,

                GroupId =
                    $"outbox-test-{Guid.NewGuid():N}",

                AutoOffsetReset =
                    AutoOffsetReset.Earliest,

                EnableAutoCommit =
                    false
            };

        using var consumer =
            new ConsumerBuilder<string, string>(
                configuration)
                .Build();

        consumer.Subscribe(Topic);

        try {
            var deadline =
                DateTimeOffset.UtcNow.AddSeconds(15);

            while (
                DateTimeOffset.UtcNow <
                deadline) {
                var result =
                    consumer.Consume(
                        TimeSpan.FromMilliseconds(250));

                if (result is null) {
                    continue;
                }

                if (result.Message.Key ==
                    expectedEventId.ToString()) {
                    return result;
                }
            }

            return null;
        }
        finally {
            consumer.Close();
        }
    }

    private async Task WaitForOutboxMessageToBePublishedAsync(
        Guid eventId) {
        await WaitForOutboxStateAsync(
            eventId,
            message =>
                message.PublishedAt.HasValue);
    }

    private async Task<OutboxMessage>
        WaitForOutboxStateAsync(
            Guid eventId,
            Func<OutboxMessage, bool> predicate) {
        var deadline =
            DateTimeOffset.UtcNow.AddSeconds(10);

        while (
            DateTimeOffset.UtcNow <
            deadline) {
            await using var dbContext =
                _fixture.CreateDbContext();

            var message =
                await dbContext.OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(
                        outboxMessage =>
                            outboxMessage.Id == eventId,
                        TestContext.Current.CancellationToken);

            if (predicate(message)) {
                return message;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken);
        }

        throw new TimeoutException(
            $"Outbox message {eventId} did not reach the expected state within the timeout.");
    }

    private static string? GetHeader(
        Headers headers,
        string name) {
        var header =
            headers.LastOrDefault(
                item =>
                    item.Key == name);

        return header is null
            ? null
            : Encoding.UTF8.GetString(
                header.GetValueBytes());
    }

    /// <summary>
    /// Kafka publisher used to deterministically simulate one transient
    /// publication failure followed by a successful retry.
    /// </summary>
    private sealed class FailOnceKafkaEventPublisher :
        IKafkaEventPublisher {
        private int _attemptCount;

        /// <summary>
        /// Gets the number of publication attempts.
        /// </summary>
        public int AttemptCount =>
            Volatile.Read(
                ref _attemptCount);

        /// <inheritdoc />
        public Task PublishAsync(
            OutboxMessage message,
            string topic,
            CancellationToken cancellationToken) {
            var attempt =
                Interlocked.Increment(
                    ref _attemptCount);

            if (attempt == 1) {
                throw new InvalidOperationException(
                    "Simulated transient Kafka publication failure.");
            }

            return Task.CompletedTask;
        }
    }
}