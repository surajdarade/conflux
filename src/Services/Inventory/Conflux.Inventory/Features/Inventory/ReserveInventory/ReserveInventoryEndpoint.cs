using Conflux.Inventory.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Inventory.Features.Inventory.ReserveInventory;

/// <summary>
/// Provides the HTTP endpoint for reserving inventory.
/// </summary>
public static class ReserveInventoryEndpoint
{
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
        InventoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
        {
            return Results.BadRequest(
                new
                {
                    error =
                        "Reservation quantity must be greater than zero."
                });
        }

        var inventoryItem = await dbContext.InventoryItems
            .SingleOrDefaultAsync(
                item => item.Id == inventoryId,
                cancellationToken);

        if (inventoryItem is null)
        {
            return Results.NotFound(
                new
                {
                    error = "Inventory item was not found."
                });
        }

        if (request.Quantity > inventoryItem.AvailableQuantity)
        {
            return Results.Conflict(
                new
                {
                    error = "Insufficient inventory."
                });
        }

        inventoryItem.Reserve(request.Quantity);

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new ReserveInventoryResponse
        {
            InventoryId = inventoryItem.Id,
            Sku = inventoryItem.Sku,
            ReservedQuantity = request.Quantity,
            AvailableQuantity =
                inventoryItem.AvailableQuantity,
            TotalReservedQuantity =
                inventoryItem.ReservedQuantity
        };

        return Results.Ok(response);
    }
}