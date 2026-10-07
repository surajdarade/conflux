using Conflux.Inventory.Application.Inventory;

namespace Conflux.Inventory.Features.Inventory.CreateInventory;

/// <summary>
/// Provides the HTTP endpoint for creating inventory items.
/// </summary>
public static class CreateInventoryEndpoint
{
    /// <summary>
    /// Maps the inventory creation endpoint to the application.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    public static void MapCreateInventoryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/inventory/items",
                HandleAsync)
            .WithName("CreateInventory")
            .WithTags("Inventory");
    }

    private static async Task<IResult> HandleAsync(
        CreateInventoryRequest request,
        InventoryApplicationService inventoryService,
        CancellationToken cancellationToken)
    {
        var result = await inventoryService.CreateAsync(
            request.Sku,
            request.AvailableQuantity,
            cancellationToken);

        if (!result.Succeeded)
        {
            return result.IsConflict
                ? Results.Conflict(new { error = result.Error })
                : Results.BadRequest(new { error = result.Error });
        }

        var response = new CreateInventoryResponse
        {
            InventoryId = result.InventoryId,
            Sku = result.Sku,
            AvailableQuantity = result.AvailableQuantity,
            ReservedQuantity = result.ReservedQuantity
        };

        return Results.Created(
            $"/api/v1/inventory/items/{result.InventoryId}",
            response);
    }
}
