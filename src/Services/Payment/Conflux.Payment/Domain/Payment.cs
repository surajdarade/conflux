namespace Conflux.Payment.Domain;

/// <summary>
/// Represents a payment associated with an order.
/// </summary>
public sealed class Payment
{
    private Payment()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Payment"/> class.
    /// </summary>
    /// <param name="id">The unique payment identifier.</param>
    /// <param name="orderId">The order associated with the payment.</param>
    /// <param name="customerId">The customer associated with the payment.</param>
    /// <param name="idempotencyKey">
    /// The idempotency key used to identify the payment operation.
    /// </param>
    /// <param name="amount">The payment amount.</param>
    /// <param name="currency">The payment currency.</param>
    public Payment(
        Guid id,
        Guid orderId,
        Guid customerId,
        string idempotencyKey,
        decimal amount,
        string currency)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment ID cannot be empty.",
                nameof(id));
        }

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Order ID cannot be empty.",
                nameof(orderId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Customer ID cannot be empty.",
                nameof(customerId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Payment amount must be greater than zero.");
        }

        Id = id;
        OrderId = orderId;
        CustomerId = customerId;
        IdempotencyKey = idempotencyKey.Trim();
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
        Status = PaymentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Gets the unique payment identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the order associated with the payment.
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    /// Gets the customer associated with the payment.
    /// </summary>
    public Guid CustomerId { get; private set; }

    /// <summary>
    /// Gets the idempotency key associated with the payment operation.
    /// </summary>
    public string IdempotencyKey { get; private set; } = null!;

    /// <summary>
    /// Gets the payment amount.
    /// </summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// Gets the three-letter ISO-style currency code.
    /// </summary>
    public string Currency { get; private set; } = null!;

    /// <summary>
    /// Gets the current payment status.
    /// </summary>
    public PaymentStatus Status { get; private set; }

    /// <summary>
    /// Gets the payment creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the timestamp of the most recent payment state update.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Marks the payment as successfully authorized.
    /// </summary>
    public void Authorize()
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Payment cannot be authorized from {Status} state.");
        }

        Status = PaymentStatus.Authorized;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the payment authorization as failed.
    /// </summary>
    public void MarkFailed()
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Payment cannot be marked as failed from {Status} state.");
        }

        Status = PaymentStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Captures an authorized payment.
    /// </summary>
    public void Capture()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Payment cannot be captured from {Status} state.");
        }

        Status = PaymentStatus.Captured;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Voids an authorized payment.
    /// </summary>
    public void Void()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Payment cannot be voided from {Status} state.");
        }

        Status = PaymentStatus.Voided;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks a captured payment as refunded.
    /// </summary>
    public void Refund()
    {
        if (Status != PaymentStatus.Captured)
        {
            throw new InvalidOperationException(
                $"Payment cannot be refunded from {Status} state.");
        }

        Status = PaymentStatus.Refunded;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}