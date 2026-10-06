using Conflux.Payment.Infrastructure;
using Conflux.Payment.OutboxPublisher.Configuration;
using Conflux.Payment.OutboxPublisher.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Conflux.Payment.OutboxPublisher;

/// <summary>
/// Continuously claims and publishes unpublished Payment Outbox messages to Kafka.
/// </summary>
public sealed class OutboxPublisherWorker :
    BackgroundService {
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IKafkaEventPublisher _kafkaPublisher;
    private readonly KafkaOptions _options;
    private readonly ILogger<OutboxPublisherWorker> _logger;
    private readonly Guid _publisherId = Guid.NewGuid();

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="OutboxPublisherWorker"/> class.
    /// </summary>
    /// <param name="scopeFactory">
    /// The service scope factory.
    /// </param>
    /// <param name="kafkaPublisher">
    /// The Kafka event publisher.
    /// </param>
    /// <param name="options">
    /// The Kafka configuration.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    public OutboxPublisherWorker(
        IServiceScopeFactory scopeFactory,
        IKafkaEventPublisher kafkaPublisher,
        IOptions<KafkaOptions> options,
        ILogger<OutboxPublisherWorker> logger) {
        _scopeFactory = scopeFactory;
        _kafkaPublisher = kafkaPublisher;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken) {
        _logger.LogInformation(
            "Payment Outbox Publisher {PublisherId} started.",
            _publisherId);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                var publishedAny =
                    await PublishBatchAsync(
                        stoppingToken);

                if (!publishedAny) {
                    await Task.Delay(
                        _options.PollInterval,
                        stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested) {
                break;
            }
            catch (Exception exception) {
                _logger.LogError(
                    exception,
                    "Unexpected error while processing Payment Outbox messages.");

                await Task.Delay(
                    _options.PollInterval,
                    stoppingToken);
            }
        }

        _logger.LogInformation(
            "Payment Outbox Publisher {PublisherId} stopped.",
            _publisherId);
    }

    private async Task<bool> PublishBatchAsync(
        CancellationToken cancellationToken) {
        using var scope =
            _scopeFactory.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<PaymentDbContext>();

        var messages =
            await ClaimBatchAsync(
                dbContext,
                cancellationToken);

        if (messages.Count == 0) {
            return false;
        }

        foreach (var message in messages) {
            cancellationToken.ThrowIfCancellationRequested();

            await PublishMessageAsync(
                dbContext,
                message,
                cancellationToken);
        }

        return true;
    }

    private async Task<List<Conflux.Outbox.OutboxMessage>>
        ClaimBatchAsync(
            PaymentDbContext dbContext,
            CancellationToken cancellationToken) {
        var now =
            DateTimeOffset.UtcNow;

        var expiredBefore =
            now - _options.ClaimLeaseDuration;

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var messages =
            await dbContext.OutboxMessages
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM outbox_messages
                    WHERE "PublishedAt" IS NULL
                      AND (
                          "ClaimedAt" IS NULL
                          OR "ClaimedAt" < {expiredBefore}
                      )
                    ORDER BY "OccurredAt"
                    LIMIT {_options.BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(
                    cancellationToken);

        if (messages.Count == 0) {
            await transaction.CommitAsync(
                cancellationToken);

            return [];
        }

        foreach (var message in messages) {
            message.Claim(
                now,
                _publisherId);
        }

        await dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return messages;
    }

    private async Task PublishMessageAsync(
        PaymentDbContext dbContext,
        Conflux.Outbox.OutboxMessage message,
        CancellationToken cancellationToken) {
        var attemptedAt =
            DateTimeOffset.UtcNow;

        message.MarkAttempted(
            attemptedAt);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        try {
            await _kafkaPublisher.PublishAsync(
                message,
                _options.PaymentEventsTopic,
                cancellationToken);

            message.MarkPublished(
                DateTimeOffset.UtcNow);

            await dbContext.SaveChangesAsync(
                cancellationToken);

            _logger.LogInformation(
                "Published Outbox message {OutboxMessageId} " +
                "with event type {EventType}.",
                message.Id,
                message.EventType);
        }
        catch (Exception exception) {
            message.MarkFailed(
                exception.Message);

            message.ReleaseClaim();

            await dbContext.SaveChangesAsync(
                cancellationToken);

            _logger.LogError(
                exception,
                "Failed to publish Outbox message {OutboxMessageId} " +
                "with event type {EventType}.",
                message.Id,
                message.EventType);
        }
    }
}