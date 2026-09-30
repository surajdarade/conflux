using Conflux.Order.Domain;

namespace Conflux.Order.Features.Orders.GetOrder;

/// <summary>
/// Represents the response returned when retrieving an order.
/// </summary>
public sealed record GetOrderResponse
{
    /// <summary>
    /// Gets the unique identifier of the order.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Gets the unique identifier of the customer who created the order.
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Gets the current lifecycle status of the order.
    /// </summary>
    public OrderStatus Status { get; init; }

    /// <summary>
    /// Gets the total monetary value of the order.
    /// </summary>
    public decimal TotalAmount { get; init; }

    /// <summary>
    /// Gets the ISO 4217 currency code used by the order.
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// Gets the UTC timestamp at which the order was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the UTC timestamp at which the order was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Gets the items contained in the order.
    /// </summary>
    public required IReadOnlyList<GetOrderItemResponse> Items { get; init; }
}

/// <summary>
/// Represents an order item returned as part of an order response.
/// </summary>
public sealed record GetOrderItemResponse
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