using Conflux.Contracts.Inventory;
using Grpc.Core;

namespace Conflux.Order.Clients.Inventory;

/// <summary>
/// Provides the Order service with access to the Inventory gRPC API.
/// </summary>
public sealed class InventoryGrpcClient :
    IInventoryClient
{
    private readonly InventoryService.InventoryServiceClient _client;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="InventoryGrpcClient"/> class.
    /// </summary>
    /// <param name="client">
    /// The generated Inventory gRPC client.
    /// </param>
    public InventoryGrpcClient(
        InventoryService.InventoryServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async Task<InventoryReservationResult> ReserveAsync(
        Guid inventoryItemId,
        Guid reservationId,
        int quantity,
        CancellationToken cancellationToken)
    {
        if (inventoryItemId == Guid.Empty)
        {
            throw new ArgumentException(
                "Inventory item ID cannot be empty.",
                nameof(inventoryItemId));
        }

        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation ID cannot be empty.",
                nameof(reservationId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Reservation quantity must be greater than zero.");
        }

        var response =
            await _client.ReserveInventoryAsync(
                new ReserveInventoryRequest
                {
                    InventoryItemId =
                        inventoryItemId.ToString(),
                    ReservationId =
                        reservationId.ToString(),
                    Quantity =
                        quantity
                },
                cancellationToken: cancellationToken);

        if (!Guid.TryParse(
                response.InventoryItemId,
                out var responseInventoryItemId) ||
            responseInventoryItemId == Guid.Empty)
        {
            throw new RpcException(
                new Status(
                    StatusCode.Internal,
                    "Inventory returned an invalid inventory item ID."));
        }

        if (!Guid.TryParse(
                response.ReservationId,
                out var responseReservationId) ||
            responseReservationId == Guid.Empty)
        {
            throw new RpcException(
                new Status(
                    StatusCode.Internal,
                    "Inventory returned an invalid reservation ID."));
        }

        return new InventoryReservationResult
        {
            InventoryItemId =
                responseInventoryItemId,
            ReservationId =
                responseReservationId,
            Sku =
                response.Sku,
            ReservedQuantity =
                response.ReservedQuantity,
            AvailableQuantity =
                response.AvailableQuantity,
            TotalReservedQuantity =
                response.TotalReservedQuantity,
            AlreadyReserved = false
        };
    }

    /// <inheritdoc />
    public async Task<InventoryReleaseResult> ReleaseAsync(
        Guid inventoryItemId,
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        if (inventoryItemId == Guid.Empty)
        {
            throw new ArgumentException(
                "Inventory item ID cannot be empty.",
                nameof(inventoryItemId));
        }

        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation ID cannot be empty.",
                nameof(reservationId));
        }

        var response =
            await _client.ReleaseInventoryAsync(
                new ReleaseInventoryRequest
                {
                    InventoryItemId =
                        inventoryItemId.ToString(),
                    ReservationId =
                        reservationId.ToString()
                },
                cancellationToken: cancellationToken);

        if (!Guid.TryParse(
                response.InventoryItemId,
                out var responseInventoryItemId) ||
            responseInventoryItemId == Guid.Empty)
        {
            throw new RpcException(
                new Status(
                    StatusCode.Internal,
                    "Inventory returned an invalid inventory item ID."));
        }

        if (!Guid.TryParse(
                response.ReservationId,
                out var responseReservationId) ||
            responseReservationId == Guid.Empty)
        {
            throw new RpcException(
                new Status(
                    StatusCode.Internal,
                    "Inventory returned an invalid reservation ID."));
        }

        return new InventoryReleaseResult
        {
            InventoryItemId =
                responseInventoryItemId,
            ReservationId =
                responseReservationId,
            Sku =
                response.Sku,
            ReleasedQuantity =
                response.ReleasedQuantity,
            AvailableQuantity =
                response.AvailableQuantity,
            TotalReservedQuantity =
                response.TotalReservedQuantity,
            AlreadyReleased =
                response.AlreadyReleased
        };
    }
}