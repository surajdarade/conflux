using System.Text.Json;
using Conflux.Contracts.Events;
using Conflux.Inventory.Domain;
using Conflux.Inventory.Infrastructure;
using Conflux.Outbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Conflux.Inventory.Application.Inventory;

/// <summary>
/// Provides the application-level operations for managing inventory
/// reservations and releases.
/// </summary>
public sealed class InventoryApplicationService
{
    private readonly InventoryDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="InventoryApplicationService"/> class.
    /// </summary>
    /// <param name="dbContext">
    /// The Inventory database context.
    /// </param>
    public InventoryApplicationService(
        InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Finds inventory by stock keeping unit.
    /// </summary>
    /// <param name="sku">
    /// The stock keeping unit to locate.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    /// <returns>
    /// The inventory lookup result.
    /// </returns>
    public async Task<GetInventoryBySkuResult> GetBySkuAsync(
        string sku,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return GetInventoryBySkuResult.Invalid(
                "SKU cannot be empty.");
        }

        var normalizedSku =
            sku.Trim().ToUpperInvariant();

        var inventory =
            await _dbContext.InventoryItems
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Sku == normalizedSku,
                    cancellationToken);

        if (inventory is null)
        {
            return GetInventoryBySkuResult.NotFound(
                $"Inventory for SKU '{normalizedSku}' was not found.");
        }

        return GetInventoryBySkuResult.Success(
            inventory.Id,
            inventory.Sku,
            inventory.AvailableQuantity,
            inventory.ReservedQuantity);
    }

    /// <summary>
    /// Attempts to reserve inventory for a business operation.
    /// </summary>
    /// <param name="inventoryItemId">
    /// The inventory item to reserve.
    /// </param>
    /// <param name="reservationId">
    /// The idempotency identifier for the reservation operation.
    /// </param>
    /// <param name="quantity">
    /// The quantity to reserve.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    /// <returns>
    /// The result of the reservation operation.
    /// </returns>
    public async Task<ReserveInventoryResult> ReserveAsync(
        Guid inventoryItemId,
        Guid reservationId,
        int quantity,
        CancellationToken cancellationToken)
    {
        if (inventoryItemId == Guid.Empty)
        {
            return ReserveInventoryResult.Invalid(
                "Inventory item ID cannot be empty.");
        }

        if (reservationId == Guid.Empty)
        {
            return ReserveInventoryResult.Invalid(
                "Reservation ID cannot be empty.");
        }

        if (quantity <= 0)
        {
            return ReserveInventoryResult.Invalid(
                "Reservation quantity must be greater than zero.");
        }

        var existingReservation =
            await _dbContext.InventoryReservations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    reservation =>
                        reservation.ReservationId ==
                        reservationId,
                    cancellationToken);

        if (existingReservation is not null)
        {
            if (existingReservation.InventoryItemId !=
                inventoryItemId)
            {
                return ReserveInventoryResult.Conflict(
                    "The reservation ID is already associated with a different inventory item.");
            }

            if (existingReservation.Quantity != quantity)
            {
                return ReserveInventoryResult.Conflict(
                    "The reservation ID is already associated with a different reservation quantity.");
            }

            return await BuildExistingReservationResultAsync(
                existingReservation,
                cancellationToken);
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var updatedRows =
            await _dbContext.InventoryItems
                .Where(
                    item =>
                        item.Id == inventoryItemId &&
                        item.AvailableQuantity >= quantity)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                item => item.AvailableQuantity,
                                item =>
                                    item.AvailableQuantity -
                                    quantity)
                            .SetProperty(
                                item => item.ReservedQuantity,
                                item =>
                                    item.ReservedQuantity +
                                    quantity)
                            .SetProperty(
                                item => item.UpdatedAt,
                                _ => DateTimeOffset.UtcNow),
                    cancellationToken);

        if (updatedRows == 0)
        {
            var inventoryExists =
                await _dbContext.InventoryItems
                    .AnyAsync(
                        item =>
                            item.Id ==
                            inventoryItemId,
                        cancellationToken);

            await transaction.RollbackAsync(
                cancellationToken);

            if (!inventoryExists)
            {
                return ReserveInventoryResult.NotFound(
                    "Inventory item was not found.");
            }

            return ReserveInventoryResult.InsufficientInventory(
                "Insufficient inventory.");
        }

        var reservation = new InventoryReservation(
            Guid.NewGuid(),
            reservationId,
            inventoryItemId,
            quantity);

        _dbContext.InventoryReservations.Add(
            reservation);

        var occurredAt = DateTimeOffset.UtcNow;
        var eventId = Guid.NewGuid();

        var inventoryItem =
            await _dbContext.InventoryItems
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id ==
                        inventoryItemId,
                    cancellationToken);

        var inventoryReservedEvent =
            new InventoryReserved
            {
                EventId = eventId,
                OccurredAt = occurredAt,
                CorrelationId = reservationId,
                CausationId = null,
                ReservationId = reservationId,
                Sku = inventoryItem.Sku,
                Quantity = quantity
            };

        var outboxMessage =
            new OutboxMessage(
                eventId,
                occurredAt,
                "inventory.reserved.v1",
                JsonSerializer.Serialize(
                    inventoryReservedEvent),
                reservationId,
                null);

        _dbContext.OutboxMessages.Add(
            outboxMessage);

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueConstraintViolation(exception))
        {
            await transaction.RollbackAsync(
                cancellationToken);

            var concurrentReservation =
                await _dbContext.InventoryReservations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        existing =>
                            existing.ReservationId ==
                            reservationId,
                        cancellationToken);

            if (concurrentReservation is null)
            {
                throw;
            }

            if (concurrentReservation.InventoryItemId !=
                inventoryItemId)
            {
                return ReserveInventoryResult.Conflict(
                    "The reservation ID is already associated with a different inventory item.");
            }

            if (concurrentReservation.Quantity !=
                quantity)
            {
                return ReserveInventoryResult.Conflict(
                    "The reservation ID is already associated with a different reservation quantity.");
            }

            return await BuildExistingReservationResultAsync(
                concurrentReservation,
                cancellationToken);
        }

        return ReserveInventoryResult.Success(
            inventoryItem.Id,
            reservation.ReservationId,
            inventoryItem.Sku,
            reservation.Quantity,
            inventoryItem.AvailableQuantity,
            inventoryItem.ReservedQuantity,
            false);
    }

    /// <summary>
    /// Attempts to release a previously created inventory reservation.
    /// </summary>
    /// <param name="inventoryItemId">
    /// The inventory item associated with the reservation.
    /// </param>
    /// <param name="reservationId">
    /// The reservation identifier.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    /// <returns>
    /// The result of the release operation.
    /// </returns>
    public async Task<ReleaseInventoryResult> ReleaseAsync(
        Guid inventoryItemId,
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        if (inventoryItemId == Guid.Empty)
        {
            return ReleaseInventoryResult.Invalid(
                "Inventory item ID cannot be empty.");
        }

        if (reservationId == Guid.Empty)
        {
            return ReleaseInventoryResult.Invalid(
                "Reservation ID cannot be empty.");
        }

        var reservation =
            await _dbContext.InventoryReservations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.ReservationId ==
                        reservationId,
                    cancellationToken);

        if (reservation is null)
        {
            return ReleaseInventoryResult.NotFound(
                "Reservation was not found.");
        }

        if (reservation.InventoryItemId !=
            inventoryItemId)
        {
            return ReleaseInventoryResult.Conflict(
                "The reservation does not belong to this inventory item.");
        }

        var inventoryItem =
            await _dbContext.InventoryItems
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        inventoryItemId,
                    cancellationToken);

        if (inventoryItem is null)
        {
            return ReleaseInventoryResult.NotFound(
                "Inventory item was not found.");
        }

        if (reservation.IsReleased)
        {
            return ReleaseInventoryResult.Success(
                inventoryItem.Id,
                reservation.ReservationId,
                inventoryItem.Sku,
                reservation.Quantity,
                inventoryItem.AvailableQuantity,
                inventoryItem.ReservedQuantity,
                true);
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var releasedRows =
            await _dbContext.InventoryReservations
                .Where(
                    candidate =>
                        candidate.ReservationId ==
                        reservationId &&
                        candidate.InventoryItemId ==
                        inventoryItemId &&
                        candidate.ReleasedAt == null)
                .ExecuteUpdateAsync(
                    setters =>
                        setters.SetProperty(
                            candidate =>
                                candidate.ReleasedAt,
                            _ => DateTimeOffset.UtcNow),
                    cancellationToken);

        if (releasedRows == 0)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            var currentReservation =
                await _dbContext.InventoryReservations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        candidate =>
                            candidate.ReservationId ==
                            reservationId,
                        cancellationToken);

            if (currentReservation is null)
            {
                return ReleaseInventoryResult.NotFound(
                    "Reservation was not found.");
            }

            if (currentReservation.InventoryItemId !=
                inventoryItemId)
            {
                return ReleaseInventoryResult.Conflict(
                    "The reservation does not belong to this inventory item.");
            }

            var currentInventory =
                await _dbContext.InventoryItems
                    .AsNoTracking()
                    .SingleAsync(
                        item =>
                            item.Id ==
                            inventoryItemId,
                        cancellationToken);

            return ReleaseInventoryResult.Success(
                currentInventory.Id,
                currentReservation.ReservationId,
                currentInventory.Sku,
                currentReservation.Quantity,
                currentInventory.AvailableQuantity,
                currentInventory.ReservedQuantity,
                true);
        }

        var updatedInventoryRows =
            await _dbContext.InventoryItems
                .Where(
                    item =>
                        item.Id ==
                        inventoryItemId &&
                        item.ReservedQuantity >=
                        reservation.Quantity)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                item =>
                                    item.AvailableQuantity,
                                item =>
                                    item.AvailableQuantity +
                                    reservation.Quantity)
                            .SetProperty(
                                item =>
                                    item.ReservedQuantity,
                                item =>
                                    item.ReservedQuantity -
                                    reservation.Quantity)
                            .SetProperty(
                                item =>
                                    item.UpdatedAt,
                                _ => DateTimeOffset.UtcNow),
                    cancellationToken);

        if (updatedInventoryRows == 0)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            return ReleaseInventoryResult.Conflict(
                "Inventory state does not contain enough reserved quantity to release this reservation.");
        }

        await transaction.CommitAsync(
            cancellationToken);

        var updatedInventoryItem =
            await _dbContext.InventoryItems
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id ==
                        inventoryItemId,
                    cancellationToken);

        return ReleaseInventoryResult.Success(
            updatedInventoryItem.Id,
            reservation.ReservationId,
            updatedInventoryItem.Sku,
            reservation.Quantity,
            updatedInventoryItem.AvailableQuantity,
            updatedInventoryItem.ReservedQuantity,
            false);
    }

    private async Task<ReserveInventoryResult>
        BuildExistingReservationResultAsync(
            InventoryReservation reservation,
            CancellationToken cancellationToken)
    {
        var inventoryItem =
            await _dbContext.InventoryItems
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        reservation.InventoryItemId,
                    cancellationToken);

        if (inventoryItem is null)
        {
            return ReserveInventoryResult.NotFound(
                "Inventory item was not found.");
        }

        return ReserveInventoryResult.Success(
            inventoryItem.Id,
            reservation.ReservationId,
            inventoryItem.Sku,
            reservation.Quantity,
            inventoryItem.AvailableQuantity,
            inventoryItem.ReservedQuantity,
            true);
    }

    private static bool IsUniqueConstraintViolation(
        DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
    }
}

/// <summary>
/// Represents the result of an inventory reservation operation.
/// </summary>
public sealed record ReserveInventoryResult
{
    private ReserveInventoryResult(
        ReserveInventoryResultStatus status,
        string error,
        Guid inventoryItemId,
        Guid reservationId,
        string sku,
        int reservedQuantity,
        int availableQuantity,
        int totalReservedQuantity,
        bool alreadyReserved)
    {
        Status = status;
        Error = error;
        InventoryItemId = inventoryItemId;
        ReservationId = reservationId;
        Sku = sku;
        ReservedQuantity = reservedQuantity;
        AvailableQuantity = availableQuantity;
        TotalReservedQuantity = totalReservedQuantity;
        AlreadyReserved = alreadyReserved;
    }

    /// <summary>
    /// Gets the result status.
    /// </summary>
    public ReserveInventoryResultStatus Status { get; }

    /// <summary>
    /// Gets the error message when the operation did not succeed.
    /// </summary>
    public string Error { get; }

    /// <summary>
    /// Gets the inventory item identifier.
    /// </summary>
    public Guid InventoryItemId { get; }

    /// <summary>
    /// Gets the reservation identifier.
    /// </summary>
    public Guid ReservationId { get; }

    /// <summary>
    /// Gets the stock keeping unit.
    /// </summary>
    public string Sku { get; }

    /// <summary>
    /// Gets the quantity reserved.
    /// </summary>
    public int ReservedQuantity { get; }

    /// <summary>
    /// Gets the currently available quantity.
    /// </summary>
    public int AvailableQuantity { get; }

    /// <summary>
    /// Gets the total currently reserved quantity.
    /// </summary>
    public int TotalReservedQuantity { get; }

    /// <summary>
    /// Gets a value indicating whether the result was produced from
    /// an existing reservation.
    /// </summary>
    public bool AlreadyReserved { get; }

    private static ReserveInventoryResult Create(
        ReserveInventoryResultStatus status,
        string error = "",
        Guid inventoryItemId = default,
        Guid reservationId = default,
        string sku = "",
        int reservedQuantity = 0,
        int availableQuantity = 0,
        int totalReservedQuantity = 0,
        bool alreadyReserved = false)
    {
        return new ReserveInventoryResult(
            status,
            error,
            inventoryItemId,
            reservationId,
            sku,
            reservedQuantity,
            availableQuantity,
            totalReservedQuantity,
            alreadyReserved);
    }

    /// <summary>
    /// Creates a successful reservation result.
    /// </summary>
    public static ReserveInventoryResult Success(
        Guid inventoryItemId,
        Guid reservationId,
        string sku,
        int reservedQuantity,
        int availableQuantity,
        int totalReservedQuantity,
        bool alreadyReserved)
    {
        return Create(
            ReserveInventoryResultStatus.Success,
            inventoryItemId: inventoryItemId,
            reservationId: reservationId,
            sku: sku,
            reservedQuantity: reservedQuantity,
            availableQuantity: availableQuantity,
            totalReservedQuantity: totalReservedQuantity,
            alreadyReserved: alreadyReserved);
    }

    /// <summary>
    /// Creates an invalid-input result.
    /// </summary>
    public static ReserveInventoryResult Invalid(
        string error)
    {
        return Create(
            ReserveInventoryResultStatus.Invalid,
            error);
    }

    /// <summary>
    /// Creates a not-found result.
    /// </summary>
    public static ReserveInventoryResult NotFound(
        string error)
    {
        return Create(
            ReserveInventoryResultStatus.NotFound,
            error);
    }

    /// <summary>
    /// Creates an insufficient-inventory result.
    /// </summary>
    public static ReserveInventoryResult InsufficientInventory(
        string error)
    {
        return Create(
            ReserveInventoryResultStatus.InsufficientInventory,
            error);
    }

    /// <summary>
    /// Creates a conflict result.
    /// </summary>
    public static ReserveInventoryResult Conflict(
        string error)
    {
        return Create(
            ReserveInventoryResultStatus.Conflict,
            error);
    }
}

/// <summary>
/// Represents the possible outcomes of an inventory reservation operation.
/// </summary>
public enum ReserveInventoryResultStatus
{
    /// <summary>
    /// The reservation succeeded.
    /// </summary>
    Success,

    /// <summary>
    /// The request contained invalid input.
    /// </summary>
    Invalid,

    /// <summary>
    /// The requested inventory item does not exist.
    /// </summary>
    NotFound,

    /// <summary>
    /// There is not enough inventory available.
    /// </summary>
    InsufficientInventory,

    /// <summary>
    /// The reservation conflicts with an existing operation.
    /// </summary>
    Conflict
}

/// <summary>
/// Represents the result of an inventory release operation.
/// </summary>
public sealed record ReleaseInventoryResult
{
    private ReleaseInventoryResult(
        ReleaseInventoryResultStatus status,
        string error,
        Guid inventoryItemId,
        Guid reservationId,
        string sku,
        int releasedQuantity,
        int availableQuantity,
        int totalReservedQuantity,
        bool alreadyReleased)
    {
        Status = status;
        Error = error;
        InventoryItemId = inventoryItemId;
        ReservationId = reservationId;
        Sku = sku;
        ReleasedQuantity = releasedQuantity;
        AvailableQuantity = availableQuantity;
        TotalReservedQuantity = totalReservedQuantity;
        AlreadyReleased = alreadyReleased;
    }

    /// <summary>
    /// Gets the result status.
    /// </summary>
    public ReleaseInventoryResultStatus Status { get; }

    /// <summary>
    /// Gets the error message when the operation did not succeed.
    /// </summary>
    public string Error { get; }

    /// <summary>
    /// Gets the inventory item identifier.
    /// </summary>
    public Guid InventoryItemId { get; }

    /// <summary>
    /// Gets the reservation identifier.
    /// </summary>
    public Guid ReservationId { get; }

    /// <summary>
    /// Gets the stock keeping unit.
    /// </summary>
    public string Sku { get; }

    /// <summary>
    /// Gets the quantity released.
    /// </summary>
    public int ReleasedQuantity { get; }

    /// <summary>
    /// Gets the currently available quantity.
    /// </summary>
    public int AvailableQuantity { get; }

    /// <summary>
    /// Gets the total currently reserved quantity.
    /// </summary>
    public int TotalReservedQuantity { get; }

    /// <summary>
    /// Gets a value indicating whether the reservation was already released.
    /// </summary>
    public bool AlreadyReleased { get; }

    private static ReleaseInventoryResult Create(
        ReleaseInventoryResultStatus status,
        string error = "",
        Guid inventoryItemId = default,
        Guid reservationId = default,
        string sku = "",
        int releasedQuantity = 0,
        int availableQuantity = 0,
        int totalReservedQuantity = 0,
        bool alreadyReleased = false)
    {
        return new ReleaseInventoryResult(
            status,
            error,
            inventoryItemId,
            reservationId,
            sku,
            releasedQuantity,
            availableQuantity,
            totalReservedQuantity,
            alreadyReleased);
    }

    /// <summary>
    /// Creates a successful release result.
    /// </summary>
    public static ReleaseInventoryResult Success(
        Guid inventoryItemId,
        Guid reservationId,
        string sku,
        int releasedQuantity,
        int availableQuantity,
        int totalReservedQuantity,
        bool alreadyReleased)
    {
        return Create(
            ReleaseInventoryResultStatus.Success,
            inventoryItemId: inventoryItemId,
            reservationId: reservationId,
            sku: sku,
            releasedQuantity: releasedQuantity,
            availableQuantity: availableQuantity,
            totalReservedQuantity: totalReservedQuantity,
            alreadyReleased: alreadyReleased);
    }

    /// <summary>
    /// Creates an invalid-input result.
    /// </summary>
    public static ReleaseInventoryResult Invalid(
        string error)
    {
        return Create(
            ReleaseInventoryResultStatus.Invalid,
            error);
    }

    /// <summary>
    /// Creates a not-found result.
    /// </summary>
    public static ReleaseInventoryResult NotFound(
        string error)
    {
        return Create(
            ReleaseInventoryResultStatus.NotFound,
            error);
    }

    /// <summary>
    /// Creates a conflict result.
    /// </summary>
    public static ReleaseInventoryResult Conflict(
        string error)
    {
        return Create(
            ReleaseInventoryResultStatus.Conflict,
            error);
    }
}

/// <summary>
/// Represents the possible outcomes of an inventory release operation.
/// </summary>
public enum ReleaseInventoryResultStatus
{
    /// <summary>
    /// The release succeeded.
    /// </summary>
    Success,

    /// <summary>
    /// The request contained invalid input.
    /// </summary>
    Invalid,

    /// <summary>
    /// The reservation or inventory item does not exist.
    /// </summary>
    NotFound,

    /// <summary>
    /// The release conflicts with the current inventory state.
    /// </summary>
    Conflict
}

/// <summary>
/// Represents the result of an inventory lookup by SKU.
/// </summary>
public sealed record GetInventoryBySkuResult
{
    private GetInventoryBySkuResult()
    {
    }

    /// <summary>
    /// Gets the result status.
    /// </summary>
    public GetInventoryBySkuResultStatus Status { get; private init; }

    /// <summary>
    /// Gets the inventory item identifier.
    /// </summary>
    public Guid InventoryItemId { get; private init; }

    /// <summary>
    /// Gets the normalized SKU.
    /// </summary>
    public string Sku { get; private init; } = string.Empty;

    /// <summary>
    /// Gets the currently available quantity.
    /// </summary>
    public int AvailableQuantity { get; private init; }

    /// <summary>
    /// Gets the currently reserved quantity.
    /// </summary>
    public int ReservedQuantity { get; private init; }

    /// <summary>
    /// Gets the error message when the operation does not succeed.
    /// </summary>
    public string? Error { get; private init; }

    /// <summary>
    /// Creates a successful lookup result.
    /// </summary>
    public static GetInventoryBySkuResult Success(
        Guid inventoryItemId,
        string sku,
        int availableQuantity,
        int reservedQuantity)
    {
        return new GetInventoryBySkuResult
        {
            Status = GetInventoryBySkuResultStatus.Success,
            InventoryItemId = inventoryItemId,
            Sku = sku,
            AvailableQuantity = availableQuantity,
            ReservedQuantity = reservedQuantity
        };
    }

    /// <summary>
    /// Creates an invalid lookup result.
    /// </summary>
    public static GetInventoryBySkuResult Invalid(
        string error)
    {
        return new GetInventoryBySkuResult
        {
            Status = GetInventoryBySkuResultStatus.Invalid,
            Error = error
        };
    }

    /// <summary>
    /// Creates a not-found lookup result.
    /// </summary>
    public static GetInventoryBySkuResult NotFound(
        string error)
    {
        return new GetInventoryBySkuResult
        {
            Status = GetInventoryBySkuResultStatus.NotFound,
            Error = error
        };
    }
}

/// <summary>
/// Represents the possible outcomes of an inventory SKU lookup.
/// </summary>
public enum GetInventoryBySkuResultStatus
{
    /// <summary>
    /// The inventory item was found.
    /// </summary>
    Success = 0,

    /// <summary>
    /// The supplied SKU was invalid.
    /// </summary>
    Invalid = 1,

    /// <summary>
    /// No inventory item exists for the supplied SKU.
    /// </summary>
    NotFound = 2
}