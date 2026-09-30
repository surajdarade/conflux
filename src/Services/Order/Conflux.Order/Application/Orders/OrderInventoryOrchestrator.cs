using System.Security.Cryptography;
using System.Text;
using Conflux.Order.Clients.Inventory;
using Conflux.Order.Domain;
using Conflux.Order.Infrastructure;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderEntity = Conflux.Order.Domain.Order;

namespace Conflux.Order.Application.Orders;

/// <summary>
/// Coordinates durable Order-side inventory reservations with the
/// Inventory service.
/// </summary>
/// <remarks>
/// The orchestrator persists reservation state locally and invokes the
/// Inventory service using deterministic reservation identifiers so retries
/// can be handled idempotently.
/// </remarks>
public sealed class OrderInventoryOrchestrator {
    /// <summary>
    /// Namespace prefix used when deriving deterministic reservation identifiers.
    /// </summary>
    private const string ReservationNamespace =
        "conflux:order-inventory-reservation:v1";

    /// <summary>
    /// The order database context.
    /// </summary>
    private readonly OrderDbContext _dbContext;

    /// <summary>
    /// The inventory service client.
    /// </summary>
    private readonly IInventoryClient _inventoryClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderInventoryOrchestrator"/> class.
    /// </summary>
    /// <param name="dbContext">The order database context.</param>
    /// <param name="inventoryClient">The inventory service client.</param>
    public OrderInventoryOrchestrator(
        OrderDbContext dbContext,
        IInventoryClient inventoryClient) {
        _dbContext = dbContext;
        _inventoryClient = inventoryClient;
    }

    /// <summary>
    /// Reserves inventory for all items in the specified order.
    /// </summary>
    /// <param name="orderId">The identifier of the order to reserve inventory for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// An <see cref="OrderInventoryOrchestrationResult"/> describing the outcome.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="orderId"/> is <see cref="Guid.Empty"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when cancellation is requested.
    /// </exception>
    public async Task<OrderInventoryOrchestrationResult>
        ReserveOrderInventoryAsync(
            Guid orderId,
            CancellationToken cancellationToken) {
        if (orderId == Guid.Empty) {
            throw new ArgumentException(
                "Order ID cannot be empty.",
                nameof(orderId));
        }

        var order = await _dbContext.Orders
            .Include(candidate => candidate.Items)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == orderId,
                cancellationToken);

        if (order is null) {
            return OrderInventoryOrchestrationResult.NotFound(
                "Order was not found.");
        }

        if (order.Status == OrderStatus.InventoryReserved) {
            return OrderInventoryOrchestrationResult.Success();
        }

        if (order.Status != OrderStatus.Pending) {
            return OrderInventoryOrchestrationResult.InvalidState(
                $"Order in {order.Status} state cannot reserve inventory.");
        }

        var reservations =
            new List<OrderInventoryReservation>();

        try {
            foreach (var orderItem in order.Items) {
                var reservation =
                    await GetOrCreateReservationAsync(
                        orderItem,
                        cancellationToken);

                if (reservation.Status ==
                    OrderInventoryReservationStatus.Reserved) {
                    reservations.Add(reservation);
                    continue;
                }

                if (reservation.Status !=
                    OrderInventoryReservationStatus.Pending) {
                    return await FailOrderAsync(
                        order,
                        reservations,
                        reservation,
                        "An order inventory reservation is in an invalid state.",
                        cancellationToken);
                }

                await _inventoryClient.ReserveAsync(
                    reservation.InventoryItemId,
                    reservation.ReservationId,
                    reservation.Quantity,
                    cancellationToken);

                reservation.MarkReserved();

                await _dbContext.SaveChangesAsync(
                    cancellationToken);

                reservations.Add(reservation);
            }

            order.MarkInventoryReserved();

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return OrderInventoryOrchestrationResult.Success();
        }
        catch (RpcException exception) {
            return await HandleInventoryFailureAsync(
                order,
                reservations,
                exception,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception exception) {
            return await HandleUnexpectedFailureAsync(
                order,
                reservations,
                exception,
                cancellationToken);
        }
    }

    /// <summary>
    /// Gets the existing reservation for an order item or creates a new one
    /// after resolving the inventory item by SKU.
    /// </summary>
    /// <param name="orderItem">The order item to reserve inventory for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The existing or newly created inventory reservation.</returns>
    private async Task<OrderInventoryReservation>
        GetOrCreateReservationAsync(
            OrderItem orderItem,
            CancellationToken cancellationToken) {
        var existingReservation =
            await _dbContext.OrderInventoryReservations
                .SingleOrDefaultAsync(
                    reservation =>
                        reservation.OrderItemId ==
                        orderItem.Id,
                    cancellationToken);

        if (existingReservation is not null) {
            return existingReservation;
        }

        var inventory =
            await _inventoryClient.GetBySkuAsync(
                orderItem.Sku,
                cancellationToken);

        var reservationId =
            CreateDeterministicReservationId(
                orderItem.OrderId,
                orderItem.Id);

        var reservation =
            new OrderInventoryReservation(
                Guid.NewGuid(),
                orderItem.Id,
                inventory.InventoryItemId,
                reservationId,
                orderItem.Quantity);

        _dbContext.OrderInventoryReservations.Add(
            reservation);

        try {
            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return reservation;
        }
        catch (DbUpdateException exception)
            when (IsUniqueConstraintViolation(exception)) {
            _dbContext.Entry(reservation).State =
                EntityState.Detached;

            var concurrentReservation =
                await _dbContext.OrderInventoryReservations
                    .SingleOrDefaultAsync(
                        existing =>
                            existing.OrderItemId ==
                            orderItem.Id,
                        cancellationToken);

            if (concurrentReservation is null) {
                throw;
            }

            return concurrentReservation;
        }
    }

    /// <summary>
    /// Handles an inventory RPC failure by compensating previously reserved
    /// inventory and marking the order as failed.
    /// </summary>
    /// <param name="order">The order being processed.</param>
    /// <param name="reservations">Reservations completed before the failure.</param>
    /// <param name="exception">The inventory RPC exception.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// An <see cref="OrderInventoryOrchestrationResult"/> describing the outcome.
    /// </returns>
    private async Task<OrderInventoryOrchestrationResult>
        HandleInventoryFailureAsync(
            OrderEntity order,
            IReadOnlyCollection<OrderInventoryReservation> reservations,
            RpcException exception,
            CancellationToken cancellationToken) {
        var compensationResult =
            await CompensateAsync(
                reservations,
                cancellationToken);

        if (!compensationResult.Succeeded) {
            return OrderInventoryOrchestrationResult.CompensationFailed(
                "Inventory reservation failed and compensation could not be completed.",
                exception.StatusCode,
                compensationResult.Error);
        }

        if (order.Status != OrderStatus.Failed) {
            order.Fail();
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return MapInventoryFailure(exception);
    }

    /// <summary>
    /// Handles an unexpected failure by compensating previously reserved
    /// inventory and marking the order as failed.
    /// </summary>
    /// <param name="order">The order being processed.</param>
    /// <param name="reservations">Reservations completed before the failure.</param>
    /// <param name="exception">The unexpected exception.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// An <see cref="OrderInventoryOrchestrationResult"/> describing the outcome.
    /// </returns>
    private async Task<OrderInventoryOrchestrationResult>
        HandleUnexpectedFailureAsync(
            OrderEntity order,
            IReadOnlyCollection<OrderInventoryReservation> reservations,
            Exception exception,
            CancellationToken cancellationToken) {
        var compensationResult =
            await CompensateAsync(
                reservations,
                cancellationToken);

        if (!compensationResult.Succeeded) {
            return OrderInventoryOrchestrationResult.CompensationFailed(
                "An unexpected inventory orchestration failure occurred and compensation could not be completed.",
                StatusCode.Internal,
                compensationResult.Error);
        }

        if (order.Status != OrderStatus.Failed) {
            order.Fail();
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return OrderInventoryOrchestrationResult.Failed(
            "Inventory orchestration failed.",
            exception.Message);
    }

    /// <summary>
    /// Fails the order and compensates all reservations associated with it.
    /// </summary>
    /// <param name="order">The order being failed.</param>
    /// <param name="reservations">Reservations completed before the failure.</param>
    /// <param name="failedReservation">The reservation that caused the failure.</param>
    /// <param name="error">The error message to return.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// An <see cref="OrderInventoryOrchestrationResult"/> describing the outcome.
    /// </returns>
    private async Task<OrderInventoryOrchestrationResult>
        FailOrderAsync(
            OrderEntity order,
            IReadOnlyCollection<OrderInventoryReservation> reservations,
            OrderInventoryReservation failedReservation,
            string error,
            CancellationToken cancellationToken) {
        var allReservations =
            reservations
                .Append(failedReservation)
                .DistinctBy(reservation => reservation.Id)
                .ToList();

        var compensationResult =
            await CompensateAsync(
                allReservations,
                cancellationToken);

        if (!compensationResult.Succeeded) {
            return OrderInventoryOrchestrationResult.CompensationFailed(
                error,
                StatusCode.FailedPrecondition,
                compensationResult.Error);
        }

        if (failedReservation.Status ==
            OrderInventoryReservationStatus.Pending) {
            failedReservation.MarkFailed();
        }

        if (order.Status != OrderStatus.Failed) {
            order.Fail();
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return OrderInventoryOrchestrationResult.Failed(error);
    }

    /// <summary>
    /// Attempts to release inventory reservations and updates their local states.
    /// </summary>
    /// <param name="reservations">The reservations to compensate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="CompensationResult"/> describing the compensation outcome.</returns>
    private async Task<CompensationResult>
        CompensateAsync(
            IReadOnlyCollection<OrderInventoryReservation> reservations,
            CancellationToken cancellationToken) {
        var errors = new List<string>();

        foreach (var reservation in reservations) {
            if (reservation.Status ==
                OrderInventoryReservationStatus.Failed ||
                reservation.Status ==
                OrderInventoryReservationStatus.Released) {
                continue;
            }

            try {
                await _inventoryClient.ReleaseAsync(
                    reservation.InventoryItemId,
                    reservation.ReservationId,
                    cancellationToken);

                if (reservation.Status ==
                    OrderInventoryReservationStatus.Reserved) {
                    reservation.MarkReleased();
                }
                else if (reservation.Status ==
                    OrderInventoryReservationStatus.Pending) {
                    reservation.MarkFailed();
                }

                await _dbContext.SaveChangesAsync(
                    cancellationToken);
            }
            catch (RpcException exception)
                when (exception.StatusCode == StatusCode.NotFound) {
                if (reservation.Status ==
                    OrderInventoryReservationStatus.Pending) {
                    reservation.MarkFailed();

                    await _dbContext.SaveChangesAsync(
                        cancellationToken);
                }
                else if (reservation.Status ==
                    OrderInventoryReservationStatus.Reserved) {
                    errors.Add(
                        $"Reservation {reservation.ReservationId} was marked reserved locally but was not found by Inventory during compensation.");
                }
            }
            catch (Exception exception) {
                errors.Add(
                    $"Failed to compensate reservation {reservation.ReservationId}: {exception.Message}");
            }
        }

        return errors.Count == 0
            ? CompensationResult.Success()
            : CompensationResult.Failure(
                string.Join(" ", errors));
    }

    /// <summary>
    /// Maps an inventory RPC exception to an orchestration result.
    /// </summary>
    /// <param name="exception">The inventory RPC exception.</param>
    /// <returns>The mapped orchestration result.</returns>
    private static OrderInventoryOrchestrationResult
        MapInventoryFailure(
            RpcException exception) {
        return exception.StatusCode switch
        {
            StatusCode.NotFound =>
                OrderInventoryOrchestrationResult.InventoryNotFound(
                    exception.Status.Detail),

            StatusCode.ResourceExhausted =>
                OrderInventoryOrchestrationResult.InventoryUnavailable(
                    exception.Status.Detail),

            StatusCode.AlreadyExists =>
                OrderInventoryOrchestrationResult.Conflict(
                    exception.Status.Detail),

            StatusCode.InvalidArgument =>
                OrderInventoryOrchestrationResult.Invalid(
                    exception.Status.Detail),

            _ =>
                OrderInventoryOrchestrationResult.Failed(
                    "Inventory service failed while processing the inventory operation.",
                    exception.Status.Detail)
        };
    }

    /// <summary>
    /// Determines whether a database update exception represents a PostgreSQL
    /// unique constraint violation.
    /// </summary>
    /// <param name="exception">The database update exception.</param>
    /// <returns>
    /// <see langword="true"/> if the exception is a unique constraint violation;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    private static bool IsUniqueConstraintViolation(
        DbUpdateException exception) {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
    }

    /// <summary>
    /// Creates a deterministic reservation identifier from the order and order item identifiers.
    /// </summary>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="orderItemId">The order item identifier.</param>
    /// <returns>A deterministic reservation identifier.</returns>
    private static Guid CreateDeterministicReservationId(
        Guid orderId,
        Guid orderItemId) {
        var source =
            string.Concat(
                ReservationNamespace,
                ":",
                orderId.ToString("N"),
                ":",
                orderItemId.ToString("N"));

        var hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(source));

        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// Represents the outcome of a compensation attempt.
    /// </summary>
    private sealed record CompensationResult {
        /// <summary>
        /// Gets a value indicating whether compensation succeeded.
        /// </summary>
        public bool Succeeded { get; private init; }

        /// <summary>
        /// Gets the error details when compensation fails.
        /// </summary>
        public string? Error { get; private init; }

        /// <summary>
        /// Creates a successful compensation result.
        /// </summary>
        /// <returns>A successful compensation result.</returns>
        public static CompensationResult Success() {
            return new CompensationResult
            {
                Succeeded = true
            };
        }

        /// <summary>
        /// Creates a failed compensation result.
        /// </summary>
        /// <param name="error">The error details.</param>
        /// <returns>A failed compensation result.</returns>
        public static CompensationResult Failure(
            string error) {
            return new CompensationResult
            {
                Succeeded = false,
                Error = error
            };
        }
    }
}

/// <summary>
/// Represents the result of Order-side inventory orchestration.
/// </summary>
public sealed record OrderInventoryOrchestrationResult {
    /// <summary>
    /// Initializes a new instance of the <see cref="OrderInventoryOrchestrationResult"/> record.
    /// </summary>
    private OrderInventoryOrchestrationResult() {
    }

    /// <summary>
    /// Gets the orchestration status.
    /// </summary>
    public OrderInventoryOrchestrationResultStatus Status {
        get;
        private init;
    }

    /// <summary>
    /// Gets the error message associated with the result, if any.
    /// </summary>
    public string? Error {
        get;
        private init;
    }

    /// <summary>
    /// Gets optional detail associated with the result.
    /// </summary>
    public string? Detail {
        get;
        private init;
    }

    /// <summary>
    /// Gets the inventory status code associated with a failure, if any.
    /// </summary>
    public StatusCode? InventoryStatusCode {
        get;
        private init;
    }

    /// <summary>
    /// Creates a successful orchestration result.
    /// </summary>
    /// <returns>A successful orchestration result.</returns>
    public static OrderInventoryOrchestrationResult Success() =>
        new()
        {
            Status =
                OrderInventoryOrchestrationResultStatus.Success
        };

    /// <summary>
    /// Creates a not-found orchestration result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>A not-found orchestration result.</returns>
    public static OrderInventoryOrchestrationResult NotFound(
        string error) =>
        new()
        {
            Status =
                OrderInventoryOrchestrationResultStatus.NotFound,
            Error = error
        };

    /// <summary>
    /// Creates an invalid-state orchestration result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>An invalid-state orchestration result.</returns>
    public static OrderInventoryOrchestrationResult InvalidState(
        string error) =>
        new()
        {
            Status =
                OrderInventoryOrchestrationResultStatus.InvalidState,
            Error = error
        };

    /// <summary>
    /// Creates an invalid orchestration result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>An invalid orchestration result.</returns>
    public static OrderInventoryOrchestrationResult Invalid(
        string error) =>
        new()
        {
            Status =
                OrderInventoryOrchestrationResultStatus.Invalid,
            Error = error
        };

    /// <summary>
    /// Creates an inventory-not-found orchestration result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>An inventory-not-found orchestration result.</returns>
    public static OrderInventoryOrchestrationResult InventoryNotFound(
        string error) =>
        new()
        {
            Status =
                OrderInventoryOrchestrationResultStatus.InventoryNotFound,
            Error = error
        };

    /// <summary>
    /// Creates an inventory-unavailable orchestration result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>An inventory-unavailable orchestration result.</returns>
    public static OrderInventoryOrchestrationResult InventoryUnavailable(
        string error) =>
        new()
        {
            Status =
                OrderInventoryOrchestrationResultStatus.InventoryUnavailable,
            Error = error
        };

    /// <summary>
    /// Creates a conflict orchestration result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>A conflict orchestration result.</returns>
    public static OrderInventoryOrchestrationResult Conflict(
        string error) =>
        new()
        {
            Status =
                OrderInventoryOrchestrationResultStatus.Conflict,
            Error = error
        };

    /// <summary>
    /// Creates a failed orchestration result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="detail">Optional error detail.</param>
    /// <returns>A failed orchestration result.</returns>
    public static OrderInventoryOrchestrationResult Failed(
        string error,
        string? detail = null) =>
        new()
        {
            Status =
                OrderInventoryOrchestrationResultStatus.Failed,
            Error = error,
            Detail = detail
        };

    /// <summary>
    /// Creates a compensation-failed orchestration result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="statusCode">The inventory status code associated with the failure.</param>
    /// <param name="detail">Optional error detail.</param>
    /// <returns>A compensation-failed orchestration result.</returns>
    public static OrderInventoryOrchestrationResult CompensationFailed(
        string error,
        StatusCode statusCode,
        string? detail) =>
        new()
        {
            Status =
                OrderInventoryOrchestrationResultStatus.CompensationFailed,
            Error = error,
            Detail = detail,
            InventoryStatusCode = statusCode
        };
}

/// <summary>
/// Represents the possible outcomes of inventory orchestration.
/// </summary>
public enum OrderInventoryOrchestrationResultStatus {
    /// <summary>
    /// Inventory was reserved successfully.
    /// </summary>
    Success = 0,

    /// <summary>
    /// The order was not found.
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// The order is not in a state that permits inventory reservation.
    /// </summary>
    InvalidState = 2,

    /// <summary>
    /// The inventory request was invalid.
    /// </summary>
    Invalid = 3,

    /// <summary>
    /// The inventory item was not found.
    /// </summary>
    InventoryNotFound = 4,

    /// <summary>
    /// The inventory is unavailable.
    /// </summary>
    InventoryUnavailable = 5,

    /// <summary>
    /// The inventory operation conflicted with existing state.
    /// </summary>
    Conflict = 6,

    /// <summary>
    /// Inventory orchestration failed.
    /// </summary>
    Failed = 7,

    /// <summary>
    /// Inventory orchestration failed and compensation could not be completed.
    /// </summary>
    CompensationFailed = 8
}