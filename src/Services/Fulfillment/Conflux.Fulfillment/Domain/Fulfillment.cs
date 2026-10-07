namespace Conflux.Fulfillment.Domain;

/// <summary>Represents the fulfillment workflow for a confirmed order.</summary>
public sealed class Fulfillment
{
    private Fulfillment() { }

    /// <summary>Creates a pending fulfillment.</summary>
    public Fulfillment(Guid id, Guid orderId, Guid customerId)
    {
        if (id == Guid.Empty) throw new ArgumentException("Fulfillment ID is required.", nameof(id));
        if (orderId == Guid.Empty) throw new ArgumentException("Order ID is required.", nameof(orderId));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer ID is required.", nameof(customerId));
        Id = id;
        OrderId = orderId;
        CustomerId = customerId;
        Status = FulfillmentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Gets the fulfillment identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the order identifier.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Gets the customer identifier.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Gets the current fulfillment status.</summary>
    public FulfillmentStatus Status { get; private set; }

    /// <summary>Gets the creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets the last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Marks the fulfillment as in progress.</summary>
    public void Start()
    {
        if (Status == FulfillmentStatus.InProgress) return;
        if (Status != FulfillmentStatus.Pending)
            throw new InvalidOperationException($"Fulfillment cannot start from {Status}.");
        Status = FulfillmentStatus.InProgress;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks the fulfillment as completed.</summary>
    public void Complete()
    {
        if (Status == FulfillmentStatus.Completed) return;
        if (Status != FulfillmentStatus.InProgress)
            throw new InvalidOperationException($"Fulfillment cannot complete from {Status}.");
        Status = FulfillmentStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
