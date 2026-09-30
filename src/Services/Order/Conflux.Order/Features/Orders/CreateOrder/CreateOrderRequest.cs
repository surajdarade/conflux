namespace Conflux.Order.Features.Orders.CreateOrder;

/// <summary>
/// Represents the request payload for creating an order.
/// </summary>
public sealed record CreateOrderRequest
{
    /// <summary>
    /// Gets the unique identifier of the customer creating the order.
    /// </summary>
    public Guid CustomerId { get; init; }

    /// <summary>
    /// Gets the items requested for the order.
    /// </summary>
    public required IReadOnlyList<CreateOrderItemRequest> Items { get; init; }
}

/// <summary>
/// Represents an item supplied when creating an order.
/// </summary>
public sealed record CreateOrderItemRequest
{
    /// <summary>
    /// Gets the stock keeping unit.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the quantity requested.
    /// </summary>
    public int Quantity { get; init; }

    /// <summary>
    /// Gets the unit price snapshot.
    /// </summary>
    public decimal UnitPrice { get; init; }

    /// <summary>
    /// Gets the ISO 4217 currency code.
    /// </summary>
    public required string Currency { get; init; }
}