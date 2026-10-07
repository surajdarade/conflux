using System.Text.Json;
using Conflux.Contracts.Events;
using Conflux.Inventory.Application.Inventory;
using Conflux.Inventory.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using InventoryTransactionalOutboxTestFixture =
    Conflux.Inventory.TransactionalOutbox.IntegrationTests.Infrastructure
        .InventoryTransactionalOutboxTestFixture;

namespace Conflux.Inventory.TransactionalOutbox.IntegrationTests;

/// <summary>
/// Verifies that Inventory reservation state and the corresponding Outbox
/// message are committed atomically to PostgreSQL.
/// </summary>
public sealed class InventoryTransactionalOutboxTests :
    IClassFixture<InventoryTransactionalOutboxTestFixture> {
    private readonly InventoryTransactionalOutboxTestFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="InventoryTransactionalOutboxTests"/> class.
    /// </summary>
    /// <param name="fixture">
    /// The shared PostgreSQL test infrastructure.
    /// </param>
    public InventoryTransactionalOutboxTests(
        InventoryTransactionalOutboxTestFixture fixture) {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies that a successful inventory reservation persists the inventory
    /// update, reservation, and InventoryReserved Outbox message together.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_PersistsReservationAndOutboxMessageAtomically() {
        var inventoryItemId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        const string sku = "CONFLUX-TRANSACTIONAL-001";
        const int initialQuantity = 100;
        const int reservationQuantity = 7;

        await SeedInventoryItemAsync(
            inventoryItemId,
            sku,
            initialQuantity);

        await using (var dbContext =
                     _fixture.CreateDbContext()) {
            var service =
                new InventoryApplicationService(
                    dbContext);

            var result =
                await service.ReserveAsync(
                    inventoryItemId,
                    reservationId,
                    reservationQuantity,
                    TestContext.Current.CancellationToken);

            result.Status
                .Should()
                .Be(ReserveInventoryResultStatus.Success);

            result.ReservationId
                .Should()
                .Be(reservationId);

            result.Sku
                .Should()
                .Be(sku);

            result.ReservedQuantity
                .Should()
                .Be(reservationQuantity);

            result.AlreadyReserved
                .Should()
                .BeFalse();
        }

        await using var verificationContext =
            _fixture.CreateDbContext();

        var inventoryItem =
            await verificationContext.InventoryItems
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id == inventoryItemId,
                    TestContext.Current.CancellationToken);

        inventoryItem.AvailableQuantity
            .Should()
            .Be(initialQuantity - reservationQuantity);

        inventoryItem.ReservedQuantity
            .Should()
            .Be(reservationQuantity);

        var reservation =
            await verificationContext.InventoryReservations
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.ReservationId == reservationId,
                    TestContext.Current.CancellationToken);

        reservation.InventoryItemId
            .Should()
            .Be(inventoryItemId);

        reservation.Quantity
            .Should()
            .Be(reservationQuantity);

        reservation.IsReleased
            .Should()
            .BeFalse();

        var outboxMessages =
            await verificationContext.OutboxMessages
                .AsNoTracking()
                .Where(
                    message =>
                        message.CorrelationId ==
                        reservationId)
                .ToListAsync(
                    TestContext.Current.CancellationToken);

        outboxMessages
            .Should()
            .ContainSingle();

        var outboxMessage =
            outboxMessages.Single();

        outboxMessage.EventType
            .Should()
            .Be("inventory.reserved.v1");

        outboxMessage.CorrelationId
            .Should()
            .Be(reservationId);

        outboxMessage.CausationId
            .Should()
            .BeNull();

        outboxMessage.PublishedAt
            .Should()
            .BeNull();

        outboxMessage.AttemptCount
            .Should()
            .Be(0);

        outboxMessage.ClaimedAt
            .Should()
            .BeNull();

        outboxMessage.ClaimedBy
            .Should()
            .BeNull();

        var publishedEvent =
            JsonSerializer.Deserialize<InventoryReserved>(
                outboxMessage.Payload);

        publishedEvent
            .Should()
            .NotBeNull();

        publishedEvent!.EventId
            .Should()
            .Be(outboxMessage.Id);

        publishedEvent.OccurredAt
            .Should()
            .BeCloseTo(
                outboxMessage.OccurredAt,
                TimeSpan.FromMilliseconds(1));

        publishedEvent.CorrelationId
            .Should()
            .Be(reservationId);

        publishedEvent.ReservationId
            .Should()
            .Be(reservationId);

        publishedEvent.Sku
            .Should()
            .Be(sku);

        publishedEvent.Quantity
            .Should()
            .Be(reservationQuantity);
    }

    /// <summary>
    /// Verifies that retrying the same reservation identifier does not create
    /// another reservation or another Outbox message.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_RetryWithSameReservationId_DoesNotCreateDuplicateOutboxMessage() {
        var inventoryItemId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        const string sku = "CONFLUX-IDEMPOTENT-001";
        const int initialQuantity = 50;
        const int reservationQuantity = 5;

        await SeedInventoryItemAsync(
            inventoryItemId,
            sku,
            initialQuantity);

        await using (var firstDbContext =
                     _fixture.CreateDbContext()) {
            var service =
                new InventoryApplicationService(
                    firstDbContext);

            var firstResult =
                await service.ReserveAsync(
                    inventoryItemId,
                    reservationId,
                    reservationQuantity,
                    TestContext.Current.CancellationToken);

            firstResult.Status
                .Should()
                .Be(ReserveInventoryResultStatus.Success);

            firstResult.AlreadyReserved
                .Should()
                .BeFalse();
        }

        await using (var retryDbContext =
                     _fixture.CreateDbContext()) {
            var service =
                new InventoryApplicationService(
                    retryDbContext);

            var retryResult =
                await service.ReserveAsync(
                    inventoryItemId,
                    reservationId,
                    reservationQuantity,
                    TestContext.Current.CancellationToken);

            retryResult.Status
                .Should()
                .Be(ReserveInventoryResultStatus.Success);

            retryResult.AlreadyReserved
                .Should()
                .BeTrue();
        }

        await using var verificationContext =
            _fixture.CreateDbContext();

        var inventoryItem =
            await verificationContext.InventoryItems
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id == inventoryItemId,
                    TestContext.Current.CancellationToken);

        inventoryItem.AvailableQuantity
            .Should()
            .Be(initialQuantity - reservationQuantity);

        inventoryItem.ReservedQuantity
            .Should()
            .Be(reservationQuantity);

        var reservationCount =
            await verificationContext.InventoryReservations
                .CountAsync(
                    reservation =>
                        reservation.ReservationId ==
                        reservationId,
                    TestContext.Current.CancellationToken);

        reservationCount
            .Should()
            .Be(1);

        var outboxCount =
            await verificationContext.OutboxMessages
                .CountAsync(
                    message =>
                        message.CorrelationId ==
                        reservationId,
                    TestContext.Current.CancellationToken);

        outboxCount
            .Should()
            .Be(1);
    }


    /// <summary>Verifies that releasing a reservation persists the release event atomically.</summary>
    [Fact]
    public async Task ReleaseAsync_PersistsReleaseOutboxMessage()
    {
        var inventoryItemId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        const string sku = "CONFLUX-RELEASE-001";

        await SeedInventoryItemAsync(inventoryItemId, sku, 20);

        await using (var db = _fixture.CreateDbContext())
        {
            var service = new InventoryApplicationService(db);
            var result = await service.ReserveAsync(
                inventoryItemId, reservationId, 4, TestContext.Current.CancellationToken);
            result.Status.Should().Be(ReserveInventoryResultStatus.Success);
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var service = new InventoryApplicationService(db);
            var result = await service.ReleaseAsync(
                inventoryItemId, reservationId, TestContext.Current.CancellationToken);
            result.Status.Should().Be(ReleaseInventoryResultStatus.Success);
            result.AlreadyReleased.Should().BeFalse();
        }

        await using var verification = _fixture.CreateDbContext();
        var message = await verification.OutboxMessages
            .AsNoTracking()
            .SingleAsync(
                item => item.EventType == "inventory.reservation-released.v1" && item.CorrelationId == reservationId,
                TestContext.Current.CancellationToken);

        var released = JsonSerializer.Deserialize<InventoryReservationReleased>(message.Payload);
        released.Should().NotBeNull();
        released!.ReservationId.Should().Be(reservationId);
        released.Sku.Should().Be(sku);
        released.Quantity.Should().Be(4);
    }

    private async Task SeedInventoryItemAsync(
        Guid inventoryItemId,
        string sku,
        int availableQuantity) {
        await using var dbContext =
            _fixture.CreateDbContext();

        var now = DateTimeOffset.UtcNow;

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO inventory_items
            (
                "Id",
                "AvailableQuantity",
                "CreatedAt",
                "ReservedQuantity",
                "Sku",
                "UpdatedAt"
            )
            VALUES
            (
                {inventoryItemId},
                {availableQuantity},
                {now},
                {0},
                {sku},
                {now}
            )
            """,
            TestContext.Current.CancellationToken);
    }
}