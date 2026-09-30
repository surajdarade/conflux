using Conflux.Order.Domain;

namespace Conflux.Order.Features.Orders.CreateOrder;

/// <summary>
/// Represents the response returned after creating an order.
/// </summary>
public sealed record CreateOrderResponse
{
    /// <summary>
    /// Gets the unique identifier of the order.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Gets the unique identifier of the customer.
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Gets the current order status.
    /// </summary>
    public OrderStatus Status { get; init; }

    /// <summary>
    /// Gets the total monetary value of the order.
    /// </summary>
    public decimal TotalAmount { get; init; }

    /// <summary>
    /// Gets the ISO 4217 currency code.
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// Gets the items belonging to the order.
    /// </summary>
    public required IReadOnlyList<CreateOrderItemResponse> Items { get; init; }
}

/// <summary>
/// Represents an order item returned as part of an order response.
/// </summary>
public sealed record CreateOrderItemResponse
{
    /// <summary>
    /// Gets the unique identifier of the order item.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets the stock keeping unit.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the quantity ordered.
    /// </summary>
    public int Quantity { get; init; }

    /// <summary>
    /// Gets the captured unit price.
    /// </summary>
    public decimal UnitPrice { get; init; }

    /// <summary>
    /// Gets the ISO 4217 currency code.
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// Gets the total price of the order line.
    /// </summary>
    public decimal LineTotal { get; init; }
}