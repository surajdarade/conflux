using Conflux.Order.Application.Orders;
using Conflux.Order.Domain;
using Conflux.Order.Infrastructure;
using Conflux.Observability;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderEntity = Conflux.Order.Domain.Order;

namespace Conflux.Order.Features.Orders.CreateOrder;

/// <summary>
/// Provides the HTTP endpoint for creating orders.
/// </summary>
public static class CreateOrderEndpoint
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>
    /// Maps the order creation endpoint to the application.
    /// </summary>
    /// <param name="endpoints">
    /// The endpoint route builder used to register the HTTP endpoint.
    /// </param>
    public static void MapCreateOrderEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/orders",
                HandleAsync)
            .WithName("CreateOrder")
            .WithTags("Orders");
    }

    private static async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        HttpRequest httpRequest,
        OrderDbContext dbContext,
        OrderInventoryOrchestrator inventoryOrchestrator,
        ConfluxBusinessMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (request.CustomerId == Guid.Empty)
        {
            return Results.BadRequest(
                new
                {
                    error = "Customer ID cannot be empty."
                });
        }

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

        var idempotencyKey = idempotencyKeyValue.ToString().Trim();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(
                new
                {
                    error =
                        $"The {IdempotencyKeyHeader} header cannot be empty."
                });
        }

        if (idempotencyKey.Length > 256)
        {
            return Results.BadRequest(
                new
                {
                    error =
                        $"The {IdempotencyKeyHeader} header cannot exceed 256 characters."
                });
        }

        if (request.Items is null ||
            request.Items.Count == 0)
        {
            return Results.BadRequest(
                new
                {
                    error = "At least one order item is required."
                });
        }

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Sku))
            {
                return Results.BadRequest(
                    new
                    {
                        error = "Every order item must contain a SKU."
                    });
            }

            if (item.Quantity <= 0)
            {
                return Results.BadRequest(
                    new
                    {
                        error =
                            "Every order item quantity must be greater than zero."
                    });
            }

            if (item.UnitPrice < 0)
            {
                return Results.BadRequest(
                    new
                    {
                        error =
                            "Order item unit price cannot be negative."
                    });
            }

            if (string.IsNullOrWhiteSpace(item.Currency))
            {
                return Results.BadRequest(
                    new
                    {
                        error =
                            "Every order item must contain a currency."
                    });
            }

            if (item.Currency.Trim().Length != 3)
            {
                return Results.BadRequest(
                    new
                    {
                        error =
                            "Currency must be a three-letter ISO 4217 code."
                    });
            }
        }

        var existingOrder = await dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .SingleOrDefaultAsync(
                order =>
                    order.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existingOrder is not null)
        {
            if (existingOrder.CustomerId != request.CustomerId)
            {
                return Results.Conflict(
                    new
                    {
                        error =
                            "The idempotency key is already associated with a different customer."
                    });
            }

            if (existingOrder.Status ==
                OrderStatus.Pending)
            {
                var existingOrderInventoryResult =
                    await inventoryOrchestrator
                        .ReserveOrderInventoryAsync(
                            existingOrder.Id,
                            cancellationToken);

                return BuildOrchestrationResult(
                    existingOrderInventoryResult,
                    existingOrder,
                    created: false,
                    metrics);
            }

            return Results.Ok(
                BuildResponse(existingOrder));
        }

        var order = new OrderEntity(
            Guid.NewGuid(),
            request.CustomerId,
            idempotencyKey);

        foreach (var item in request.Items)
        {
            var orderItem = new OrderItem(
                Guid.NewGuid(),
                item.Sku,
                item.Quantity,
                item.UnitPrice,
                item.Currency);

            order.AddItem(orderItem);
        }

        dbContext.Orders.Add(order);

        try
        {
            await dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueConstraintViolation(exception))
        {
            foreach (var item in order.Items.ToList()) {
                dbContext.Entry(item).State = EntityState.Detached;
            }

            dbContext.Entry(order).State = EntityState.Detached;

            var concurrentOrder = await dbContext.Orders
                .AsNoTracking()
                .Include(existing => existing.Items)
                .SingleOrDefaultAsync(
                    existing =>
                        existing.IdempotencyKey ==
                        idempotencyKey,
                    cancellationToken);

            if (concurrentOrder is null)
            {
                throw;
            }

            if (concurrentOrder.CustomerId !=
                request.CustomerId)
            {
                return Results.Conflict(
                    new
                    {
                        error =
                            "The idempotency key is already associated with a different customer."
                    });
            }

            if (concurrentOrder.Status ==
                OrderStatus.Pending)
            {
                var concurrentOrderInventoryResult =
                    await inventoryOrchestrator
                        .ReserveOrderInventoryAsync(
                            concurrentOrder.Id,
                            cancellationToken);

                var refreshedConcurrentOrder =
                    await dbContext.Orders
                        .AsNoTracking()
                        .Include(existing => existing.Items)
                        .SingleAsync(
                            existing => existing.Id == concurrentOrder.Id,
                            cancellationToken);

                return BuildOrchestrationResult(
                    concurrentOrderInventoryResult,
                    refreshedConcurrentOrder,
                    created: false,
                    metrics);
            }

            return Results.Ok(
                BuildResponse(concurrentOrder));
        }

        var inventoryResult =
            await inventoryOrchestrator
                .ReserveOrderInventoryAsync(
                    order.Id,
                    cancellationToken);

        return BuildOrchestrationResult(
            inventoryResult,
            order,
            created: true,
            metrics);
    }

    private static IResult BuildOrchestrationResult(
        OrderInventoryOrchestrationResult result,
        OrderEntity order,
        bool created,
        ConfluxBusinessMetrics metrics)
    {
        if (created)
        {
            if (result.Status == OrderInventoryOrchestrationResultStatus.Success)
            {
                metrics.OrderCreated();
            }
            else
            {
                metrics.OrderFailed();
            }
        }

        return result.Status switch
        {
            OrderInventoryOrchestrationResultStatus.Success =>
                created
                    ? Results.Created(
                        $"/api/v1/orders/{order.Id}",
                        BuildResponse(order))
                    : Results.Ok(
                        BuildResponse(order)),

            OrderInventoryOrchestrationResultStatus.NotFound =>
                Results.NotFound(
                    new
                    {
                        error = result.Error
                    }),

            OrderInventoryOrchestrationResultStatus.InvalidState =>
                Results.Conflict(
                    new
                    {
                        error = result.Error
                    }),

            OrderInventoryOrchestrationResultStatus.Invalid =>
                Results.BadRequest(
                    new
                    {
                        error = result.Error
                    }),

            OrderInventoryOrchestrationResultStatus.InventoryNotFound =>
                Results.NotFound(
                    new
                    {
                        error = result.Error,
                        orderId = order.Id,
                        status = order.Status
                    }),

            OrderInventoryOrchestrationResultStatus.InventoryUnavailable =>
                Results.Conflict(
                    new
                    {
                        error = result.Error,
                        orderId = order.Id,
                        status = order.Status
                    }),

            OrderInventoryOrchestrationResultStatus.Conflict =>
                Results.Conflict(
                    new
                    {
                        error = result.Error,
                        orderId = order.Id,
                        status = order.Status
                    }),

            OrderInventoryOrchestrationResultStatus.Failed =>
                Results.StatusCode(
                    StatusCodes.Status502BadGateway),

            OrderInventoryOrchestrationResultStatus.CompensationFailed =>
                Results.StatusCode(
                    StatusCodes.Status500InternalServerError),

            _ =>
                Results.StatusCode(
                    StatusCodes.Status500InternalServerError)
        };
    }

    private static CreateOrderResponse BuildResponse(
        OrderEntity order)
    {
        return new CreateOrderResponse
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Status = order.Status,
            TotalAmount = order.TotalAmount,
            Currency = order.Currency,
            Items = order.Items
                .Select(
                    item =>
                        new CreateOrderItemResponse
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

    private static bool IsUniqueConstraintViolation(
        DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
    }
}