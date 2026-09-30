using Conflux.Inventory.Domain;
using Conflux.Inventory.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Inventory.Features.Inventory.CreateInventory;

/// <summary>
/// Provides the HTTP endpoint for creating inventory items.
/// </summary>
public static class CreateInventoryEndpoint
{
    /// <summary>
    /// Maps the inventory creation endpoint to the application.
    /// </summary>
    /// <param name="endpoints">
    /// The endpoint route builder used to register the HTTP endpoint.
    /// </param>
    public static void MapCreateInventoryEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/inventory/items",
                HandleAsync)
            .WithName("CreateInventory")
            .WithTags("Inventory");
    }

    private static async Task<IResult> HandleAsync(
        CreateInventoryRequest request,
        InventoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return Results.BadRequest(
                new
                {
                    error = "SKU is required."
                });
        }

        if (request.AvailableQuantity < 0)
        {
            return Results.BadRequest(
                new
                {
                    error = "Available quantity cannot be negative."
                });
        }

        var sku = request.Sku.Trim().ToUpperInvariant();

        var exists = await dbContext.InventoryItems
            .AnyAsync(
                item => item.Sku == sku,
                cancellationToken);

        if (exists)
        {
            return Results.Conflict(
                new
                {
                    error = "Inventory already exists for this SKU."
                });
        }

        var inventoryItem = new InventoryItem(
            Guid.NewGuid(),
            sku,
            request.AvailableQuantity);

        dbContext.InventoryItems.Add(inventoryItem);

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new CreateInventoryResponse
        {
            InventoryId = inventoryItem.Id,
            Sku = inventoryItem.Sku,
            AvailableQuantity =
                inventoryItem.AvailableQuantity,
            ReservedQuantity =
                inventoryItem.ReservedQuantity
        };

        return Results.Created(
            $"/api/v1/inventory/items/{inventoryItem.Id}",
            response);
    }
}