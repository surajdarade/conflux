namespace Conflux.Payment.Features.Payments.AuthorizePayment;

/// <summary>
/// Represents an HTTP request to authorize a payment.
/// </summary>
public sealed record AuthorizePaymentRequest
{
    /// <summary>
    /// Gets the order identifier associated with the payment.
    /// </summary>
    public Guid OrderId { get; init; }

    /// <summary>
    /// Gets the customer identifier associated with the payment.
    /// </summary>
    public Guid CustomerId { get; init; }

    /// <summary>
    /// Gets the payment amount.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// Gets the payment currency.
    /// </summary>
    public string Currency { get; init; } = string.Empty;
}