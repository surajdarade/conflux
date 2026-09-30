using Conflux.Inventory.Application.Inventory;

namespace Conflux.Inventory.Features.Inventory.ReleaseInventory;

/// <summary>
/// Provides the HTTP endpoint for releasing inventory reservations.
/// </summary>
public static class ReleaseInventoryEndpoint
{
    /// <summary>
    /// Maps the inventory reservation release endpoint to the application.
    /// </summary>
    /// <param name="endpoints">
    /// The endpoint route builder used to register the HTTP endpoint.
    /// </param>
    public static void MapReleaseInventoryEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/inventory/items/{inventoryId:guid}/reservations/{reservationId:guid}/release",
                HandleAsync)
            .WithName("ReleaseInventory")
            .WithTags("Inventory");
    }

    private static async Task<IResult> HandleAsync(
        Guid inventoryId,
        Guid reservationId,
        InventoryApplicationService inventoryService,
        CancellationToken cancellationToken)
    {
        var result =
            await inventoryService.ReleaseAsync(
                inventoryId,
                reservationId,
                cancellationToken);

        return result.Status switch
        {
            ReleaseInventoryResultStatus.Success =>
                Results.Ok(
                    new ReleaseInventoryResponse
                    {
                        InventoryId =
                            result.InventoryItemId,
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
                    }),

            ReleaseInventoryResultStatus.Invalid =>
                Results.BadRequest(
                    new
                    {
                        error = result.Error
                    }),

            ReleaseInventoryResultStatus.NotFound =>
                Results.NotFound(
                    new
                    {
                        error = result.Error
                    }),

            ReleaseInventoryResultStatus.Conflict =>
                Results.Conflict(
                    new
                    {
                        error = result.Error
                    }),

            _ =>
                throw new InvalidOperationException(
                    $"Unsupported release result status: {result.Status}.")
        };
    }
}