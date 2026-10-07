using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Conflux.Contracts.Events;
using Conflux.Fulfillment.Domain;
using Conflux.Fulfillment.Infrastructure;
using Conflux.Inbox;
using Conflux.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using FullfillmentEntity = Conflux.Fulfillment.Domain.Fulfillment;

namespace Conflux.Fulfillment.Consumer;

/// <summary>Consumes confirmed-order events and creates fulfillment records.</summary>
public sealed class FulfillmentConsumer : BackgroundService {
    private const string ConsumerName = "fulfillment.order-confirmed.v1";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FulfillmentConsumerOptions _options;
    private readonly KafkaRetryPublisher _retryPublisher;
    private readonly ILogger<FulfillmentConsumer> _logger;

    /// <summary>Initializes the consumer.</summary>
    public FulfillmentConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<FulfillmentConsumerOptions> options,
        KafkaRetryPublisher retryPublisher,
        ILogger<FulfillmentConsumer> logger) {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _retryPublisher = retryPublisher;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.OrderEventsTopic);

        try {
            while (!stoppingToken.IsCancellationRequested) {
                ConsumeResult<string, string>? result = null;

                try {
                    result = consumer.Consume(stoppingToken);

                    await ProcessAsync(
                        consumer,
                        result,
                        stoppingToken);
                }
                catch (ConsumeException exception) {
                    _logger.LogError(
                        exception,
                        "Fulfillment Kafka consumption failed.");
                }
                catch (Exception exception) {
                    _logger.LogError(
                        exception,
                        "Fulfillment event processing failed.");

                    if (result is not null) {
                        await _retryPublisher.PublishFailureAsync(
                            result,
                            exception,
                            stoppingToken);

                        consumer.Commit(result);
                    }
                }
            }
        }
        finally {
            consumer.Close();
        }
    }

    private async Task ProcessAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> result,
        CancellationToken cancellationToken) {
        using var activity = KafkaTrace.StartConsumerActivity(
            result,
            "conflux.kafka.consume");

        var eventType = Header(
            result.Message.Headers,
            "event-type");

        if (!string.Equals(
                eventType,
                "order.confirmed.v1",
                StringComparison.Ordinal)) {
            consumer.Commit(result);
            return;
        }

        var eventId = Guid.Parse(
            Header(
                result.Message.Headers,
                "event-id"));

        var correlationId = Guid.Parse(
            Header(
                result.Message.Headers,
                "correlation-id"));

        var causationHeader = Header(
            result.Message.Headers,
            "causation-id");

        Guid? causationId =
            Guid.TryParse(
                causationHeader,
                out var parsed)
                ? parsed
                : null;

        await using var scope =
            _scopeFactory.CreateAsyncScope();

        var db =
            scope.ServiceProvider
                .GetRequiredService<FulfillmentDbContext>();

        var inbox =
            await db.InboxMessages.SingleOrDefaultAsync(
                item =>
                    item.Id == eventId &&
                    item.ConsumerName == ConsumerName,
                cancellationToken);

        if (inbox?.ProcessedAt is not null) {
            consumer.Commit(result);
            return;
        }

        if (inbox is null) {
            inbox = new InboxMessage(
                eventId,
                ConsumerName,
                eventType,
                result.Message.Value,
                correlationId,
                causationId);

            db.InboxMessages.Add(inbox);

            try {
                await db.SaveChangesAsync(
                    cancellationToken);
            }
            catch (DbUpdateException) {
                db.Entry(inbox).State =
                    EntityState.Detached;

                inbox =
                    await db.InboxMessages.SingleAsync(
                        item =>
                            item.Id == eventId &&
                            item.ConsumerName == ConsumerName,
                        cancellationToken);
            }
        }

        try {
            var message =
                JsonSerializer.Deserialize<OrderConfirmed>(
                    result.Message.Value)
                ?? throw new InvalidOperationException(
                    "OrderConfirmed payload was empty.");

            var fulfillment =
                await db.Fulfillments.SingleOrDefaultAsync(
                    item => item.OrderId == message.OrderId,
                    cancellationToken);

            if (fulfillment is null) {
                fulfillment =
                    new FullfillmentEntity(
                        Guid.NewGuid(),
                        message.OrderId,
                        message.CustomerId);

                db.Fulfillments.Add(fulfillment);
            }

            fulfillment.Start();

            await db.SaveChangesAsync(
                cancellationToken);

            inbox.MarkProcessed(
                DateTimeOffset.UtcNow);

            await db.SaveChangesAsync(
                cancellationToken);

            consumer.Commit(result);
        }
        catch (Exception exception) {
            inbox.MarkFailed(
                exception.Message);

            await db.SaveChangesAsync(
                cancellationToken);

            throw;
        }
    }

    private static string Header(
        Headers headers,
        string name) {
        for (var index = headers.Count - 1;
             index >= 0;
             index--) {
            var header = headers[index];

            if (!string.Equals(
                    header.Key,
                    name,
                    StringComparison.Ordinal)) {
                continue;
            }

            var value = header.GetValueBytes();

            return value is { Length: > 0 }
                ? Encoding.UTF8.GetString(value)
                : string.Empty;
        }

        return string.Empty;
    }
}

/// <summary>Configuration for the Fulfillment Kafka consumer.</summary>
public sealed class FulfillmentConsumerOptions {
    /// <summary>Gets or sets the Kafka bootstrap servers.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Gets or sets the Order event topic.</summary>
    public string OrderEventsTopic { get; set; } = "conflux.order.events";

    /// <summary>Gets or sets the consumer group.</summary>
    public string ConsumerGroup { get; set; } =
        "conflux-fulfillment-v1";

    /// <summary>Gets or sets the failure retry delay.</summary>
    public TimeSpan FailureDelay { get; set; } =
        TimeSpan.FromSeconds(2);
}