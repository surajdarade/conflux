using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Conflux.Contracts.Events;
using Conflux.Inbox;
using Conflux.Kafka;
using Conflux.Observability;
using Conflux.Outbox;
using System.Security.Cryptography;
using Conflux.Order.Domain;
using Conflux.Order.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Conflux.Order.Saga;

/// <summary>
/// Drives the checkout saga from durable Kafka events.
/// </summary>
/// <remarks>
/// The worker uses the Order Inbox to make event handling idempotent. External
/// Payment calls are themselves idempotent, so a crash between an external call
/// and Inbox completion can safely be retried.
/// </remarks>
public sealed class CheckoutSagaWorker : BackgroundService
{
    private const string ConsumerName = "order.checkout-saga.v1";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CheckoutSagaOptions _options;
    private readonly KafkaRetryPublisher _retryPublisher;
    private readonly ConfluxBusinessMetrics _metrics;
    private readonly ILogger<CheckoutSagaWorker> _logger;

    /// <summary>Initializes a new instance of the worker.</summary>
    public CheckoutSagaWorker(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IOptions<CheckoutSagaOptions> options,
        KafkaRetryPublisher retryPublisher,
        ConfluxBusinessMetrics metrics,
        ILogger<CheckoutSagaWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _retryPublisher = retryPublisher;
        _metrics = metrics;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnablePartitionEof = false,
            AllowAutoCreateTopics = true
        };

        using var consumer = new ConsumerBuilder<string, string>(configuration).Build();
        consumer.Subscribe(new[] { _options.OrderEventsTopic, _options.PaymentEventsTopic });

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;
                try
                {
                    result = consumer.Consume(stoppingToken);
                    await ProcessAsync(consumer, result, stoppingToken);
                }
                catch (ConsumeException exception)
                {
                    _logger.LogError(exception, "Kafka checkout saga consumption failed.");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Checkout saga message processing failed for {Topic} partition {Partition} offset {Offset}.", result?.Topic, result?.Partition, result?.Offset);
                    _metrics.SagaFailed();
                    if (result is not null)
                    {
                        await _retryPublisher.PublishFailureAsync(result, exception, stoppingToken);
                        consumer.Commit(result);
                    }
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task ProcessAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> result,
        CancellationToken cancellationToken)
    {
        using var activity = KafkaTrace.StartConsumerActivity(result, "conflux.kafka.consume");
        var eventType = GetHeader(result.Message.Headers, "event-type");
        var eventIdText = GetHeader(result.Message.Headers, "event-id");
        var correlationIdText = GetHeader(result.Message.Headers, "correlation-id");
        var causationIdText = GetHeader(result.Message.Headers, "causation-id");

        if (!Guid.TryParse(eventIdText, out var eventId) ||
            !Guid.TryParse(correlationIdText, out var correlationId))
        {
            throw new InvalidOperationException("Kafka message is missing valid event-id or correlation-id headers.");
        }

        Guid? causationId = Guid.TryParse(causationIdText, out var parsedCausationId)
            ? parsedCausationId
            : null;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var inbox = await dbContext.InboxMessages
            .SingleOrDefaultAsync(
                message => message.Id == eventId && message.ConsumerName == ConsumerName,
                cancellationToken);

        if (inbox?.ProcessedAt is not null)
        {
            consumer.Commit(result);
            return;
        }

        if (inbox is null)
        {
            inbox = new InboxMessage(
                eventId,
                ConsumerName,
                eventType,
                result.Message.Value,
                correlationId,
                causationId);
            dbContext.InboxMessages.Add(inbox);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                dbContext.Entry(inbox).State = EntityState.Detached;
                inbox = await dbContext.InboxMessages.SingleAsync(
                    message => message.Id == eventId && message.ConsumerName == ConsumerName,
                    cancellationToken);
            }
        }

        try
        {
            switch (eventType)
            {
                case "order.inventory-reserved.v1":
                    await HandleOrderInventoryReservedAsync(result.Message.Value, cancellationToken);
                    break;
                case "payment.authorized.v1":
                    await HandlePaymentAuthorizedAsync(result.Message.Value, cancellationToken);
                    break;
                case "payment.captured.v1":
                    await HandlePaymentCapturedAsync(result.Message.Value, cancellationToken);
                    break;
                case "payment.voided.v1":
                    await HandlePaymentVoidedAsync(result.Message.Value, cancellationToken);
                    break;
                default:
                    _logger.LogWarning("Ignoring unsupported checkout saga event type {EventType}.", eventType);
                    break;
            }

            inbox.MarkProcessed(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            consumer.Commit(result);
        }
        catch (Exception exception)
        {
            inbox.MarkFailed(exception.Message);
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task HandleOrderInventoryReservedAsync(string payload, CancellationToken cancellationToken)
    {
        var orderEvent = Deserialize<OrderInventoryReserved>(payload);
        var client = _httpClientFactory.CreateClient("Payment");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/authorize")
        {
            Content = JsonContent.Create(new
            {
                orderEvent.OrderId,
                orderEvent.CustomerId,
                orderEvent.TotalAmount,
                Currency = orderEvent.Currency
            })
        };
        request.Headers.Add("Idempotency-Key", $"conflux-payment-authorize:{orderEvent.OrderId:N}");

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Payment authorization failed with {(int)response.StatusCode}: {body}");
        }
    }

    private async Task HandlePaymentAuthorizedAsync(string payload, CancellationToken cancellationToken)
    {
        var paymentEvent = Deserialize<PaymentAuthorized>(payload);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            candidate => candidate.Id == paymentEvent.OrderId,
            cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException($"Order {paymentEvent.OrderId} was not found.");
        }

        order.RecordPaymentAuthorization(paymentEvent.PaymentId);
        await dbContext.SaveChangesAsync(cancellationToken);

        var client = _httpClientFactory.CreateClient("Payment");
        using var response = await client.PostAsync(
            $"/api/v1/payments/{paymentEvent.PaymentId}/capture",
            content: null,
            cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Payment capture failed with {(int)response.StatusCode}: {body}");
        }
    }

    private async Task HandlePaymentCapturedAsync(string payload, CancellationToken cancellationToken)
    {
        var paymentEvent = Deserialize<PaymentCaptured>(payload);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            candidate => candidate.Id == paymentEvent.OrderId,
            cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException($"Order {paymentEvent.OrderId} was not found.");
        }

        order.ConfirmPaymentCapture(paymentEvent.PaymentId);

        var occurredAt = DateTimeOffset.UtcNow;
        var confirmedEvent = new OrderConfirmed
        {
            EventId = CreateDeterministicEventId(order.Id, "confirmed"),
            OccurredAt = occurredAt,
            CorrelationId = order.Id,
            CausationId = paymentEvent.EventId,
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            PaymentId = paymentEvent.PaymentId,
            TotalAmount = order.TotalAmount,
            Currency = order.Currency
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.OutboxMessages.Add(new OutboxMessage(
            confirmedEvent.EventId,
            occurredAt,
            "order.confirmed.v1",
            JsonSerializer.Serialize(confirmedEvent),
            confirmedEvent.CorrelationId,
            confirmedEvent.CausationId));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task HandlePaymentVoidedAsync(string payload, CancellationToken cancellationToken)
    {
        var paymentEvent = Deserialize<PaymentVoided>(payload);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            candidate => candidate.Id == paymentEvent.OrderId,
            cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException($"Order {paymentEvent.OrderId} was not found.");
        }

        order.CancelAfterPaymentVoid(paymentEvent.PaymentId);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Guid CreateDeterministicEventId(Guid orderId, string eventName)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"conflux:order-event:v1:{eventName}:{orderId:N}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static T Deserialize<T>(string payload)
    {
        return JsonSerializer.Deserialize<T>(payload)
            ?? throw new InvalidOperationException($"Could not deserialize {typeof(T).Name} event payload.");
    }

    /// <summary>
    /// Gets the most recently added Kafka header with the specified name.
    /// </summary>
    /// <param name="headers">The Kafka message headers.</param>
    /// <param name="name">The header name.</param>
    /// <returns>
    /// The UTF-8 decoded header value, or an empty string when the header
    /// does not exist or has no value.
    /// </returns>
    private static string GetHeader(
        Headers headers,
        string name) {
        for (var index = headers.Count - 1; index >= 0; index--) {
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

/// <summary>Configuration for the Order checkout saga consumer.</summary>
public sealed class CheckoutSagaOptions
{
    /// <summary>Gets or sets whether the checkout saga worker is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets the Kafka bootstrap servers.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Gets the Order event topic.</summary>
    public string OrderEventsTopic { get; set; } = "conflux.order.events";

    /// <summary>Gets the Payment event topic.</summary>
    public string PaymentEventsTopic { get; set; } = "conflux.payment.events";

    /// <summary>Gets the Kafka consumer group.</summary>
    public string ConsumerGroup { get; set; } = "conflux-order-checkout-saga-v1";

    /// <summary>Gets the delay used after a processing failure.</summary>
    public TimeSpan FailureDelay { get; set; } = TimeSpan.FromSeconds(2);
}
