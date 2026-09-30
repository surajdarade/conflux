using Conflux.Order.Infrastructure;
using Microsoft.EntityFrameworkCore;
using OrderEntity = Conflux.Order.Domain.Order;

namespace Conflux.Order.Features.Orders.GetOrder;

/// <summary>
/// Provides the HTTP endpoint for retrieving orders.
/// </summary>
public static class GetOrderEndpoint
{
    /// <summary>
    /// Maps the order retrieval endpoint to the application.
    /// </summary>
    /// <param name="endpoints">
    /// The endpoint route builder used to register the HTTP endpoint.
    /// </param>
    public static void MapGetOrderEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/orders/{orderId:guid}",
                HandleAsync)
            .WithName("GetOrder")
            .WithTags("Orders");
    }

    private static async Task<IResult> HandleAsync(
        Guid orderId,
        OrderDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(candidate => candidate.Items)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == orderId,
                cancellationToken);

        if (order is null)
        {
            return Results.NotFound(
                new
                {
                    error = "Order was not found."
                });
        }

        return Results.Ok(
            BuildResponse(order));
    }

    private static GetOrderResponse BuildResponse(
        OrderEntity order)
    {
        return new GetOrderResponse
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Status = order.Status,
            TotalAmount = order.TotalAmount,
            Currency = order.Currency,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            Items = order.Items
                .Select(
                    item =>
                        new GetOrderItemResponse
                        {
                            ItemId = item.Id,
                            Sku = item.Sku,
                            Quantity = item.Quantity,
                            UnitPrice = item.UnitPrice,
                            Currency = item.Currency,
                            LineTotal = item.LineTotal
                        })
                .ToList()
        };
    }
}