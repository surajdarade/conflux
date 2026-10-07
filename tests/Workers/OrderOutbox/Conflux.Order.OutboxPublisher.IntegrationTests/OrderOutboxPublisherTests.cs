using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Conflux.Contracts.Events;
using Conflux.Order.Infrastructure;
using Conflux.Order.OutboxPublisher;
using Conflux.Order.OutboxPublisher.Configuration;
using Conflux.Order.OutboxPublisher.Kafka;
using Conflux.Outbox;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using OrderOutboxPublisherTestFixture =
    Conflux.Order.OutboxPublisher.IntegrationTests.Infrastructure
        .OrderOutboxPublisherTestFixture;

namespace Conflux.Order.OutboxPublisher.IntegrationTests;

/// <summary>
/// Verifies Order Outbox Publisher behavior against real PostgreSQL
/// and Kafka containers.
/// </summary>
public sealed class OrderOutboxPublisherTests :
    IClassFixture<OrderOutboxPublisherTestFixture> {
    private const string Topic =
        "conflux.order.events";

    private readonly OrderOutboxPublisherTestFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="OrderOutboxPublisherTests"/> class.
    /// </summary>
    /// <param name="fixture">
    /// The shared test infrastructure.
    /// </param>
    public OrderOutboxPublisherTests(
        OrderOutboxPublisherTestFixture fixture) {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies that an unpublished Outbox message is published to Kafka
    /// and subsequently marked as published.
    /// </summary>
    [Fact]
    public async Task PublishesOutboxMessageToKafka_AndMarksMessagePublished() {
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var inventoryEvent =
            new OrderInventoryReserved
            {
                EventId = eventId,
                OccurredAt = occurredAt,
                CorrelationId = orderId,
                CausationId = null,
                OrderId = orderId,
                CustomerId = customerId,
                TotalAmount = 200m,
                Currency = "INR"
            };

        await using (var dbContext =
                     _fixture.CreateDbContext()) {
            var message =
                new OutboxMessage(
                    eventId,
                    occurredAt,
                    "order.inventory-reserved.v1",
                    JsonSerializer.Serialize(
                        inventoryEvent),
                    orderId,
                    null);

            dbContext.OutboxMessages.Add(message);

            await dbContext.SaveChangesAsync(
                TestContext.Current.CancellationToken);
        }

        await CreateKafkaTopicAsync();

        await using var serviceProvider =
            CreatePublisherServiceProvider();

        var worker =
            serviceProvider.GetRequiredService<
                OutboxPublisherWorker>();

        using var cancellationTokenSource =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(15));

        var workerTask =
            worker.StartAsync(
                cancellationTokenSource.Token);

        try {
            var publishedMessage =
                await ConsumeMessageAsync(
                    eventId.ToString(),
                    cancellationTokenSource.Token);

            publishedMessage.Should().NotBeNull();

            publishedMessage.Message.Key
                .Should()
                .Be(eventId.ToString());

            publishedMessage.Message.Value
                .Should()
                .Be(
                    JsonSerializer.Serialize(
                        inventoryEvent));

            publishedMessage.Message.Headers
                .GetLastBytes("event-type")
                .Should()
                .NotBeNull();

            Encoding.UTF8.GetString(
                    publishedMessage.Message.Headers
                        .GetLastBytes("event-type")!)
                .Should()
                .Be("order.inventory-reserved.v1");

            Encoding.UTF8.GetString(
                    publishedMessage.Message.Headers
                        .GetLastBytes("event-id")!)
                .Should()
                .Be(eventId.ToString());

            Encoding.UTF8.GetString(
                    publishedMessage.Message.Headers
                        .GetLastBytes("correlation-id")!)
                .Should()
                .Be(orderId.ToString());

            await WaitForOutboxStateAsync(
                eventId,
                message =>
                    message.PublishedAt.HasValue &&
                    message.AttemptCount == 1,
                cancellationTokenSource.Token);

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

            persistedMessage.ClaimedAt
                .Should()
                .BeNull();

            persistedMessage.ClaimedBy
                .Should()
                .BeNull();
        }
        finally {
            await cancellationTokenSource.CancelAsync();
            await workerTask;
        }
    }

    /// <summary>
    /// Verifies that a failed publication releases the claim and is retried
    /// successfully.
    /// </summary>
    [Fact]
    public async Task FailedPublication_IsRetried_AndEventuallyMarkedPublished() {
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        await using (var dbContext =
                     _fixture.CreateDbContext()) {
            var orderInventoryReservedEvent =
                new OrderInventoryReserved
                {
                    EventId = eventId,
                    OccurredAt = occurredAt,
                    CorrelationId = orderId,
                    CausationId = null,
                    OrderId = orderId,
                    CustomerId = customerId,
                    TotalAmount = 100m,
                    Currency = "INR"
                };

            var message =
                new OutboxMessage(
                    eventId,
                    occurredAt,
                    "order.inventory-reserved.v1",
                    JsonSerializer.Serialize(
                        orderInventoryReservedEvent),
                    orderId,
                    null);

            dbContext.OutboxMessages.Add(message);

            await dbContext.SaveChangesAsync(
                TestContext.Current.CancellationToken);
        }

        var fakePublisher =
            new FailOnceKafkaEventPublisher();

        await using var serviceProvider =
            CreatePublisherServiceProvider(
                fakePublisher);

        var worker =
            serviceProvider.GetRequiredService<
                OutboxPublisherWorker>();

        using var cancellationTokenSource =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(10));

        var workerTask =
            worker.StartAsync(
                cancellationTokenSource.Token);

        try {
            await WaitForOutboxStateAsync(
                eventId,
                message =>
                    message.PublishedAt.HasValue &&
                    message.AttemptCount >= 2,
                cancellationTokenSource.Token);

            fakePublisher.AttemptCount
                .Should()
                .BeGreaterThanOrEqualTo(2);

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
                .BeGreaterThanOrEqualTo(2);

            persistedMessage.LastError
                .Should()
                .BeNull();

            persistedMessage.ClaimedAt
                .Should()
                .BeNull();

            persistedMessage.ClaimedBy
                .Should()
                .BeNull();
        }
        finally {
            await cancellationTokenSource.CancelAsync();
            await workerTask;
        }
    }

    private async Task CreateKafkaTopicAsync() {
        var adminConfiguration =
            new AdminClientConfig
            {
                BootstrapServers =
                    _fixture.KafkaBootstrapAddress
            };

        using var adminClient =
            new AdminClientBuilder(
                adminConfiguration)
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
                exception.Results.All(
                    result =>
                        result.Error.Code ==
                        ErrorCode.TopicAlreadyExists)) {
        }
    }

    private async Task<ConsumeResult<string, string>>
        ConsumeMessageAsync(
            string expectedKey,
            CancellationToken cancellationToken) {
        var consumerConfiguration =
            new ConsumerConfig
            {
                BootstrapServers =
                    _fixture.KafkaBootstrapAddress,
                GroupId =
                    $"order-outbox-test-{Guid.NewGuid():N}",
                AutoOffsetReset =
                    AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

        using var consumer =
            new ConsumerBuilder<string, string>(
                consumerConfiguration)
                .Build();

        consumer.Subscribe(Topic);

        while (!cancellationToken.IsCancellationRequested) {
            var result =
                consumer.Consume(
                    TimeSpan.FromMilliseconds(100));

            if (result?.Message is not null &&
                result.Message.Key == expectedKey) {
                return result;
            }
        }

        throw new TimeoutException(
            $"Kafka message with key '{expectedKey}' was not published.");
    }

    private ServiceProvider
        CreatePublisherServiceProvider(
            IKafkaEventPublisher? publisher = null) {
        var services =
            new ServiceCollection();

        services.AddLogging(
            builder =>
                builder.SetMinimumLevel(
                    LogLevel.Warning));

        services.AddOptions<KafkaOptions>()
            .Configure(
                options =>
                {
                    options.BootstrapServers =
                        _fixture.KafkaBootstrapAddress;

                    options.OrderEventsTopic =
                        Topic;

                    options.BatchSize = 100;

                    options.PollInterval =
                        TimeSpan.FromMilliseconds(100);

                    options.ClaimLeaseDuration =
                        TimeSpan.FromSeconds(1);
                });

        services.AddDbContext<OrderDbContext>(
            options =>
                options.UseNpgsql(
                    _fixture.PostgresConnectionString));

        services.AddSingleton<IKafkaEventPublisher>(
            publisher ??
            new Conflux.Order.OutboxPublisher.Kafka
                .ConfluentKafkaEventPublisher(
                    _fixture.KafkaBootstrapAddress));

        services.AddSingleton<
            OutboxPublisherWorker>();

        services.AddSingleton<
            Microsoft.Extensions.Hosting.IHostedService>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    OutboxPublisherWorker>());

        return services.BuildServiceProvider();
    }

    private async Task WaitForOutboxStateAsync(
        Guid eventId,
        Func<OutboxMessage, bool> predicate,
        CancellationToken cancellationToken) {
        var deadline =
            DateTimeOffset.UtcNow +
            TimeSpan.FromSeconds(8);

        while (DateTimeOffset.UtcNow < deadline) {
            await using var dbContext =
                _fixture.CreateDbContext();

            var message =
                await dbContext.OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(
                        candidate =>
                            candidate.Id == eventId,
                        cancellationToken);

            if (predicate(message)) {
                return;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(100),
                cancellationToken);
        }

        throw new TimeoutException(
            $"Outbox message {eventId} did not reach expected state.");
    }

    private sealed class FailOnceKafkaEventPublisher :
        IKafkaEventPublisher {
        private int _attemptCount;

        public int AttemptCount =>
            Volatile.Read(
                ref _attemptCount);

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