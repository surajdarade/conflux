namespace Conflux.Order.Domain;

/// <summary>
/// Represents a customer order managed by the Order service.
/// </summary>
public sealed class Order
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Order"/> class.
    /// </summary>
    /// <param name="id">
    /// The unique identifier of the order.
    /// </param>
    /// <param name="customerId">
    /// The unique identifier of the customer who created the order.
    /// </param>
    /// <param name="idempotencyKey">
    /// The client-provided idempotency key for the order creation operation.
    /// </param>
    public Order(
        Guid id,
        Guid customerId,
        string idempotencyKey)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Customer ID cannot be empty.",
                nameof(customerId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        Id = id;
        CustomerId = customerId;
        IdempotencyKey = idempotencyKey.Trim();
        Status = OrderStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Gets the unique identifier of the order.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the unique identifier of the customer who created the order.
    /// </summary>
    public Guid CustomerId { get; private set; }

    /// <summary>
    /// Gets the idempotency key used to create the order.
    /// </summary>
    public string IdempotencyKey { get; private set; } = null!;

    /// <summary>
    /// Gets the current lifecycle status of the order.
    /// </summary>
    public OrderStatus Status { get; private set; }

    /// <summary>
    /// Gets the total monetary value of the order.
    /// </summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>
    /// Gets the ISO 4217 currency code used by the order.
    /// </summary>
    public string Currency { get; private set; } = null!;

    /// <summary>
    /// Gets the UTC timestamp at which the order was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which the order was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Gets the items contained in the order.
    /// </summary>
    public IReadOnlyCollection<OrderItem> Items =>
        _items.AsReadOnly();

    /// <summary>
    /// Adds an item to the order.
    /// </summary>
    /// <param name="item">
    /// The item to add.
    /// </param>
    public void AddItem(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException(
                "Items can only be added while the order is pending.");
        }

        if (_items.Count > 0 &&
            !string.Equals(
                Currency,
                item.Currency,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "All order items must use the same currency.");
        }

        if (_items.Count == 0)
        {
            Currency = item.Currency;
        }

        _items.Add(item);

        TotalAmount += item.LineTotal;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the order as having successfully reserved inventory.
    /// </summary>
    public void MarkInventoryReserved()
    {
        TransitionTo(
            OrderStatus.InventoryReserved,
            OrderStatus.Pending);
    }

    /// <summary>
    /// Marks the order as waiting for payment processing.
    /// </summary>
    public void MarkPaymentPending()
    {
        TransitionTo(
            OrderStatus.PaymentPending,
            OrderStatus.InventoryReserved);
    }

    /// <summary>
    /// Marks the order as successfully confirmed.
    /// </summary>
    public void Confirm()
    {
        TransitionTo(
            OrderStatus.Confirmed,
            OrderStatus.PaymentPending);
    }

    /// <summary>
    /// Cancels the order.
    /// </summary>
    /// <remarks>
    /// An order may be cancelled while it is still being processed.
    /// Terminal orders cannot be cancelled.
    /// </remarks>
    public void Cancel()
    {
        if (Status is
            OrderStatus.Confirmed or
            OrderStatus.Cancelled or
            OrderStatus.Failed)
        {
            throw new InvalidOperationException(
                $"An order in {Status} state cannot be cancelled.");
        }

        Status = OrderStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the order as failed.
    /// </summary>
    /// <remarks>
    /// A failed order cannot transition back into an active processing state.
    /// </remarks>
    public void Fail()
    {
        if (Status is
            OrderStatus.Confirmed or
            OrderStatus.Cancelled or
            OrderStatus.Failed)
        {
            throw new InvalidOperationException(
                $"An order in {Status} state cannot be marked as failed.");
        }

        Status = OrderStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void TransitionTo(
        OrderStatus targetStatus,
        OrderStatus expectedCurrentStatus)
    {
        if (Status != expectedCurrentStatus)
        {
            throw new InvalidOperationException(
                $"Order cannot transition from {Status} to {targetStatus}.");
        }

        Status = targetStatus;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}