namespace Conflux.Order.Domain;

/// <summary>
/// Represents a line item belonging to an order.
/// </summary>
public sealed class OrderItem
{
    private OrderItem()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderItem"/> class.
    /// </summary>
    /// <param name="id">
    /// The unique identifier of the order item.
    /// </param>
    /// <param name="sku">
    /// The stock keeping unit purchased by the customer.
    /// </param>
    /// <param name="quantity">
    /// The quantity purchased.
    /// </param>
    /// <param name="unitPrice">
    /// The price of one unit captured when the order was created.
    /// </param>
    /// <param name="currency">
    /// The ISO 4217 currency code for the unit price.
    /// </param>
    public OrderItem(
        Guid id,
        string sku,
        int quantity,
        decimal unitPrice,
        string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Order item quantity must be greater than zero.");
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice),
                "Order item price cannot be negative.");
        }

        Id = id;
        Sku = sku.Trim().ToUpperInvariant();
        Quantity = quantity;
        UnitPrice = unitPrice;
        Currency = currency.Trim().ToUpperInvariant();
        LineTotal = unitPrice * quantity;
    }

    /// <summary>
    /// Gets the unique identifier of the order item.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the stock keeping unit purchased by the customer.
    /// </summary>
    public string Sku { get; private set; } = null!;

    /// <summary>
    /// Gets the quantity purchased.
    /// </summary>
    public int Quantity { get; private set; }

    /// <summary>
    /// Gets the unit price captured when the order was created.
    /// </summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>
    /// Gets the ISO 4217 currency code for the unit price.
    /// </summary>
    public string Currency { get; private set; } = null!;

    /// <summary>
    /// Gets the total price for this order line.
    /// </summary>
    public decimal LineTotal { get; private set; }

    /// <summary>
    /// Gets the unique identifier of the order containing this item.
    /// </summary>
    public Guid OrderId { get; private set; }
}