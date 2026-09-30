using Conflux.Inventory.Application.Inventory;

namespace Conflux.Inventory.Features.Inventory.ReserveInventory;

/// <summary>
/// Provides the HTTP endpoint for reserving inventory.
/// </summary>
public static class ReserveInventoryEndpoint
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>
    /// Maps the inventory reservation endpoint to the application.
    /// </summary>
    /// <param name="endpoints">
    /// The endpoint route builder used to register the HTTP endpoint.
    /// </param>
    public static void MapReserveInventoryEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/inventory/items/{inventoryId:guid}/reservations",
                HandleAsync)
            .WithName("ReserveInventory")
            .WithTags("Inventory");
    }

    private static async Task<IResult> HandleAsync(
        Guid inventoryId,
        ReserveInventoryRequest request,
        HttpRequest httpRequest,
        InventoryApplicationService inventoryService,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue(
                IdempotencyKeyHeader,
                out var idempotencyKeyValue))
        {
            return Results.BadRequest(
                new
                {
                    error =
                        $"The {IdempotencyKeyHeader} header is required."
                });
        }

        if (!Guid.TryParse(
                idempotencyKeyValue.ToString(),
                out var reservationId) ||
            reservationId == Guid.Empty)
        {
            return Results.BadRequest(
                new
                {
                    error =
                        $"The {IdempotencyKeyHeader} header must contain a valid non-empty GUID."
                });
        }

        var result =
            await inventoryService.ReserveAsync(
                inventoryId,
                reservationId,
                request.Quantity,
                cancellationToken);

        return result.Status switch
        {
            ReserveInventoryResultStatus.Success =>
                Results.Ok(
                    new ReserveInventoryResponse
                    {
                        InventoryId =
                            result.InventoryItemId,
                        Sku =
                            result.Sku,
                        ReservedQuantity =
                            result.ReservedQuantity,
                        AvailableQuantity =
                            result.AvailableQuantity,
                        TotalReservedQuantity =
                            result.TotalReservedQuantity
                    }),

            ReserveInventoryResultStatus.Invalid =>
                Results.BadRequest(
                    new
                    {
                        error = result.Error
                    }),

            ReserveInventoryResultStatus.NotFound =>
                Results.NotFound(
                    new
                    {
                        error = result.Error
                    }),

            ReserveInventoryResultStatus.InsufficientInventory =>
                Results.Conflict(
                    new
                    {
                        error = result.Error
                    }),

            ReserveInventoryResultStatus.Conflict =>
                Results.Conflict(
                    new
                    {
                        error = result.Error
                    }),

            _ =>
                throw new InvalidOperationException(
                    $"Unsupported reservation result status: {result.Status}.")
        };
    }
}