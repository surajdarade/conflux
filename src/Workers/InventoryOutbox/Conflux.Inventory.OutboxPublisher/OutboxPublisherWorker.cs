using Conflux.Inventory.Infrastructure;
using Conflux.Inventory.Infrastructure.Sharding;
using Conflux.Inventory.OutboxPublisher.Configuration;
using Conflux.Inventory.OutboxPublisher.Kafka;
using Conflux.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Conflux.Inventory.OutboxPublisher;

/// <summary>
/// Continuously claims and publishes unpublished Inventory Outbox
/// messages to Kafka.
/// </summary>
public sealed class OutboxPublisherWorker :
    BackgroundService {
    private readonly InventoryDbContextProvider _dbContextProvider;
    private readonly IKafkaEventPublisher _kafkaPublisher;
    private readonly KafkaOptions _options;
    private readonly ILogger<OutboxPublisherWorker> _logger;
    private readonly Guid _publisherId = Guid.NewGuid();

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="OutboxPublisherWorker"/> class.
    /// </summary>
    /// <param name="dbContextProvider">
    /// The provider used to route Outbox polling across physical inventory shards.
    /// </param>
    /// <param name="kafkaPublisher">
    /// The Kafka event publisher.
    /// </param>
    /// <param name="options">
    /// The Kafka publisher options.
    /// </param>
    /// <param name="logger">
    /// The worker logger.
    /// </param>
    public OutboxPublisherWorker(
        InventoryDbContextProvider dbContextProvider,
        IKafkaEventPublisher kafkaPublisher,
        IOptions<KafkaOptions> options,
        ILogger<OutboxPublisherWorker> logger) {
        _dbContextProvider = dbContextProvider;
        _kafkaPublisher = kafkaPublisher;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken) {
        _logger.LogInformation(
            "Inventory Outbox Publisher {PublisherId} started.",
            _publisherId);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                var claimedAny =
                    await PublishBatchAsync(
                        stoppingToken);

                if (!claimedAny) {
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
                    "Unexpected error while processing Inventory Outbox messages.");

                await Task.Delay(
                    _options.PollInterval,
                    stoppingToken);
            }
        }

        _logger.LogInformation(
            "Inventory Outbox Publisher {PublisherId} stopped.",
            _publisherId);
    }

    private async Task<bool> PublishBatchAsync(
        CancellationToken cancellationToken) {
        var claimedAny = false;

        foreach (var shardName in _dbContextProvider.ShardNames) {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug(
                "Polling Inventory Outbox shard {ShardName}.",
                shardName);

            await using var dbContext =
                _dbContextProvider.CreateForShard(
                    shardName);

            var messages =
                await ClaimBatchAsync(
                    dbContext,
                    cancellationToken);

            if (messages.Count == 0) {
                continue;
            }

            claimedAny = true;

            _logger.LogInformation(
                "Claimed {MessageCount} Inventory Outbox messages from shard {ShardName}.",
                messages.Count,
                shardName);

            foreach (var message in messages) {
                cancellationToken.ThrowIfCancellationRequested();

                await PublishMessageAsync(
                    dbContext,
                    message,
                    cancellationToken);
            }
        }

        return claimedAny;
    }

    private async Task<List<OutboxMessage>> ClaimBatchAsync(
        InventoryDbContext dbContext,
        CancellationToken cancellationToken) {
        var executionStrategy =
            dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(
            async () =>
            {
                // A retry can reuse the same DbContext after a transient
                // failure. Clear state before starting a new transactional
                // unit of work.
                dbContext.ChangeTracker.Clear();

                var now =
                    DateTimeOffset.UtcNow;

                var expiredBefore =
                    now -
                    _options.ClaimLeaseDuration;

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
            });
    }

    private async Task PublishMessageAsync(
        InventoryDbContext dbContext,
        OutboxMessage message,
        CancellationToken cancellationToken) {
        message.MarkAttempted(
            DateTimeOffset.UtcNow);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        try {
            await _kafkaPublisher.PublishAsync(
                message,
                _options.InventoryEventsTopic,
                cancellationToken);

            message.MarkPublished(
                DateTimeOffset.UtcNow);

            await dbContext.SaveChangesAsync(
                cancellationToken);

            _logger.LogInformation(
                "Published Inventory Outbox message {OutboxMessageId} with event type {EventType}.",
                message.Id,
                message.EventType);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception exception) {
            message.MarkFailed(
                exception.Message);

            message.ReleaseClaim();

            await dbContext.SaveChangesAsync(
                cancellationToken);

            _logger.LogError(
                exception,
                "Failed to publish Inventory Outbox message {OutboxMessageId} with event type {EventType}.",
                message.Id,
                message.EventType);
        }
    }
}