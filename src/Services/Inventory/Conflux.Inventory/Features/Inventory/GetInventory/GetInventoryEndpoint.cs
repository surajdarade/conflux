using Conflux.Inventory.Infrastructure.Sharding;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Inventory.Features.Inventory.GetInventory;

/// <summary>
/// Provides the HTTP endpoint for retrieving inventory items.
/// </summary>
public static class GetInventoryEndpoint
{
    /// <summary>Maps the inventory retrieval endpoint.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    public static void MapGetInventoryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/inventory/items/{inventoryId:guid}",
                HandleAsync)
            .WithName("GetInventory")
            .WithTags("Inventory");
    }

    private static async Task<IResult> HandleAsync(
        Guid inventoryId,
        InventoryDbContextProvider dbContextProvider,
        CancellationToken cancellationToken)
    {
        await using var dbContext = dbContextProvider.CreateForInventoryId(inventoryId);
        var inventoryItem = await dbContext.InventoryItems
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == inventoryId, cancellationToken);

        if (inventoryItem is null)
        {
            return Results.NotFound(new { error = "Inventory item was not found." });
        }

        return Results.Ok(new GetInventoryResponse
        {
            InventoryId = inventoryItem.Id,
            Sku = inventoryItem.Sku,
            AvailableQuantity = inventoryItem.AvailableQuantity,
            ReservedQuantity = inventoryItem.ReservedQuantity,
            CreatedAt = inventoryItem.CreatedAt,
            UpdatedAt = inventoryItem.UpdatedAt
        });
    }
}
