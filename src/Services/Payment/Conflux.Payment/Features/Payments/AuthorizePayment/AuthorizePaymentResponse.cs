using Conflux.Payment.Domain;

namespace Conflux.Payment.Features.Payments.AuthorizePayment;

/// <summary>
/// Represents the HTTP response for a payment authorization.
/// </summary>
public sealed record AuthorizePaymentResponse
{
    /// <summary>
    /// Gets the payment identifier.
    /// </summary>
    public Guid PaymentId { get; init; }

    /// <summary>
    /// Gets the order identifier.
    /// </summary>
    public Guid OrderId { get; init; }

    /// <summary>
    /// Gets the customer identifier.
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

    /// <summary>
    /// Gets the payment status.
    /// </summary>
    public PaymentStatus Status { get; init; }

    /// <summary>
    /// Gets a value indicating whether this response represents
    /// an already-authorized payment.
    /// </summary>
    public bool AlreadyAuthorized { get; init; }
}