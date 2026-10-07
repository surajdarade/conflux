using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Conflux.Catalog.Infrastructure;
using Conflux.Catalog.ReadModel;
using Conflux.Contracts.Events;
using Conflux.Inbox;
using Conflux.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Conflux.Catalog.ProjectionWorker;

/// <summary>
/// Consumes Catalog product events and maintains the read projection.
/// </summary>
public sealed class ProductProjectionWorker : BackgroundService {
    private const string ConsumerName =
        "catalog.product-projection.v1";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CatalogProjectionOptions _options;
    private readonly KafkaRetryPublisher _retryPublisher;
    private readonly ILogger<ProductProjectionWorker> _logger;

    /// <summary>
    /// Initializes the worker.
    /// </summary>
    /// <param name="scopeFactory">
    /// The service scope factory.
    /// </param>
    /// <param name="options">
    /// The projection worker options.
    /// </param>
    /// <param name="retryPublisher"></param>
    /// <param name="logger">
    /// The worker logger.
    /// </param>
    public ProductProjectionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<CatalogProjectionOptions> options,
        KafkaRetryPublisher retryPublisher,
        ILogger<ProductProjectionWorker> logger) {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _retryPublisher = retryPublisher;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken) {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true
        };

        using var consumer =
            new ConsumerBuilder<string, string>(config)
                .Build();

        consumer.Subscribe(_options.Topic);

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
                catch (Exception exception)
                    when (exception is not OperationCanceledException) {
                    _logger.LogError(
                        exception,
                        "Catalog projection processing failed.");

                    if (result is not null)
                    {
                        await _retryPublisher.PublishFailureAsync(result, exception, stoppingToken);
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
        using var activity = KafkaTrace.StartConsumerActivity(result, "conflux.kafka.consume");
        var eventType =
            Header(
                result.Message.Headers,
                "event-type");

        if (!string.Equals(
                eventType,
                "catalog.product-created.v1",
                StringComparison.Ordinal)) {
            consumer.Commit(result);
            return;
        }

        var eventIdText =
            Header(
                result.Message.Headers,
                "event-id");

        var correlationIdText =
            Header(
                result.Message.Headers,
                "correlation-id");

        var causationText =
            Header(
                result.Message.Headers,
                "causation-id");

        if (!Guid.TryParse(
                eventIdText,
                out var eventId) ||
            !Guid.TryParse(
                correlationIdText,
                out var correlationId)) {
            throw new InvalidOperationException(
                "Kafka message is missing valid event-id or correlation-id headers.");
        }

        Guid? causationId =
            Guid.TryParse(
                causationText,
                out var parsed)
                ? parsed
                : null;

        await using var scope =
            _scopeFactory.CreateAsyncScope();

        var db =
            scope.ServiceProvider
                .GetRequiredService<CatalogDbContext>();

        var inbox =
            await db.Set<InboxMessage>()
                .SingleOrDefaultAsync(
                    item =>
                        item.Id == eventId &&
                        item.ConsumerName == ConsumerName,
                    cancellationToken);

        if (inbox?.ProcessedAt is not null) {
            consumer.Commit(result);
            return;
        }

        if (inbox is null) {
            inbox =
                new InboxMessage(
                    eventId,
                    ConsumerName,
                    eventType,
                    result.Message.Value,
                    correlationId,
                    causationId);

            db.Set<InboxMessage>().Add(inbox);

            try {
                await db.SaveChangesAsync(
                    cancellationToken);
            }
            catch (DbUpdateException) {
                db.Entry(inbox).State =
                    EntityState.Detached;

                inbox =
                    await db.Set<InboxMessage>()
                        .SingleAsync(
                            item =>
                                item.Id == eventId &&
                                item.ConsumerName == ConsumerName,
                            cancellationToken);
            }
        }

        try {
            var message =
                JsonSerializer.Deserialize<ProductCreated>(
                    result.Message.Value)
                ?? throw new InvalidOperationException(
                    "ProductCreated payload was empty.");

            var projection =
                await db.ProductReadModels
                    .SingleOrDefaultAsync(
                        item =>
                            item.ProductId ==
                            message.ProductId,
                        cancellationToken);

            if (projection is null) {
                db.ProductReadModels.Add(
                    new ProductReadModel(
                        message.ProductId,
                        message.Sku,
                        message.Name,
                        message.Description,
                        message.Price,
                        message.Currency,
                        message.IsActive,
                        message.CreatedAt,
                        message.UpdatedAt));
            }
            else {
                projection.Apply(
                    message.Name,
                    message.Description,
                    message.Price,
                    message.Currency,
                    message.IsActive,
                    message.CreatedAt,
                    message.UpdatedAt);
            }

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

    /// <summary>
    /// Gets the most recently added Kafka header with the specified name.
    /// </summary>
    /// <param name="headers">
    /// The Kafka message headers.
    /// </param>
    /// <param name="name">
    /// The header name.
    /// </param>
    /// <returns>
    /// The UTF-8 decoded header value, or an empty string when the header
    /// does not exist or has no value.
    /// </returns>
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

            var value =
                header.GetValueBytes();

            return value is { Length: > 0 }
                ? Encoding.UTF8.GetString(value)
                : string.Empty;
        }

        return string.Empty;
    }
}

/// <summary>
/// Configuration for the Catalog projection worker.
/// </summary>
public sealed class CatalogProjectionOptions {
    /// <summary>
    /// Gets or sets Kafka bootstrap servers.
    /// </summary>
    public string BootstrapServers { get; set; } =
        "localhost:9092";

    /// <summary>
    /// Gets or sets the Catalog topic.
    /// </summary>
    public string Topic { get; set; } =
        "conflux.catalog.events";

    /// <summary>
    /// Gets or sets the consumer group.
    /// </summary>
    public string ConsumerGroup { get; set; } =
        "conflux-catalog-projection-v1";

    /// <summary>
    /// Gets or sets retry delay.
    /// </summary>
    public TimeSpan FailureDelay { get; set; } =
        TimeSpan.FromSeconds(2);
}