using Conflux.Contracts.Inventory;
using Conflux.Inventory.Application.Inventory;
using Grpc.Core;

namespace Conflux.Inventory.Grpc;

/// <summary>
/// Provides the gRPC API for inventory operations.
/// </summary>
public sealed class InventoryGrpcService :
    InventoryService.InventoryServiceBase
{
    private readonly InventoryApplicationService _inventoryService;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="InventoryGrpcService"/> class.
    /// </summary>
    /// <param name="inventoryService">
    /// The Inventory application service.
    /// </param>
    public InventoryGrpcService(
        InventoryApplicationService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    /// <summary>
    /// Reserves inventory for a business operation.
    /// </summary>
    /// <param name="request">
    /// The reservation request.
    /// </param>
    /// <param name="context">
    /// The gRPC server call context.
    /// </param>
    /// <returns>
    /// The reservation result.
    /// </returns>
    public override async Task<ReserveInventoryResponse>
        ReserveInventory(
            ReserveInventoryRequest request,
            ServerCallContext context)
    {
        if (!Guid.TryParse(
                request.InventoryItemId,
                out var inventoryItemId) ||
            inventoryItemId == Guid.Empty)
        {
            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "Inventory item ID must be a valid non-empty GUID."));
        }

        if (!Guid.TryParse(
                request.ReservationId,
                out var reservationId) ||
            reservationId == Guid.Empty)
        {
            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "Reservation ID must be a valid non-empty GUID."));
        }

        var result =
            await _inventoryService.ReserveAsync(
                inventoryItemId,
                reservationId,
                request.Quantity,
                context.CancellationToken);

        return result.Status switch
        {
            ReserveInventoryResultStatus.Success =>
                new ReserveInventoryResponse
                {
                    InventoryItemId =
                        result.InventoryItemId.ToString(),
                    ReservationId =
                        result.ReservationId.ToString(),
                    Sku =
                        result.Sku,
                    ReservedQuantity =
                        result.ReservedQuantity,
                    AvailableQuantity =
                        result.AvailableQuantity,
                    TotalReservedQuantity =
                        result.TotalReservedQuantity
                },

            ReserveInventoryResultStatus.Invalid =>
                throw new RpcException(
                    new Status(
                        StatusCode.InvalidArgument,
                        result.Error)),

            ReserveInventoryResultStatus.NotFound =>
                throw new RpcException(
                    new Status(
                        StatusCode.NotFound,
                        result.Error)),

            ReserveInventoryResultStatus.InsufficientInventory =>
                throw new RpcException(
                    new Status(
                        StatusCode.ResourceExhausted,
                        result.Error)),

            ReserveInventoryResultStatus.Conflict =>
                throw new RpcException(
                    new Status(
                        StatusCode.AlreadyExists,
                        result.Error)),

            _ =>
                throw new RpcException(
                    new Status(
                        StatusCode.Internal,
                        "Unsupported reservation result status."))
        };
    }

    /// <summary>
    /// Releases a previously created inventory reservation.
    /// </summary>
    /// <param name="request">
    /// The release request.
    /// </param>
    /// <param name="context">
    /// The gRPC server call context.
    /// </param>
    /// <returns>
    /// The release result.
    /// </returns>
    public override async Task<ReleaseInventoryResponse>
        ReleaseInventory(
            ReleaseInventoryRequest request,
            ServerCallContext context)
    {
        if (!Guid.TryParse(
                request.InventoryItemId,
                out var inventoryItemId) ||
            inventoryItemId == Guid.Empty)
        {
            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "Inventory item ID must be a valid non-empty GUID."));
        }

        if (!Guid.TryParse(
                request.ReservationId,
                out var reservationId) ||
            reservationId == Guid.Empty)
        {
            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "Reservation ID must be a valid non-empty GUID."));
        }

        var result =
            await _inventoryService.ReleaseAsync(
                inventoryItemId,
                reservationId,
                context.CancellationToken);

        return result.Status switch
        {
            ReleaseInventoryResultStatus.Success =>
                new ReleaseInventoryResponse
                {
                    InventoryItemId =
                        result.InventoryItemId.ToString(),
                    ReservationId =
                        result.ReservationId.ToString(),
                    Sku =
                        result.Sku,
                    ReleasedQuantity =
                        result.ReleasedQuantity,
                    AvailableQuantity =
                        result.AvailableQuantity,
                    TotalReservedQuantity =
                        result.TotalReservedQuantity,
                    AlreadyReleased =
                        result.AlreadyReleased
                },

            ReleaseInventoryResultStatus.Invalid =>
                throw new RpcException(
                    new Status(
                        StatusCode.InvalidArgument,
                        result.Error)),

            ReleaseInventoryResultStatus.NotFound =>
                throw new RpcException(
                    new Status(
                        StatusCode.NotFound,
                        result.Error)),

            ReleaseInventoryResultStatus.Conflict =>
                throw new RpcException(
                    new Status(
                        StatusCode.FailedPrecondition,
                        result.Error)),

            _ =>
                throw new RpcException(
                    new Status(
                        StatusCode.Internal,
                        "Unsupported release result status."))
        };
    }
}