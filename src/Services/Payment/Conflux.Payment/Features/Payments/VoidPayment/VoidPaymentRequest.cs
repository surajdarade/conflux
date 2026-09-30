namespace Conflux.Payment.Features.Payments.VoidPayment;

/// <summary>
/// Represents an HTTP request to void an authorized payment.
/// </summary>
public sealed record VoidPaymentRequest
{
    /// <summary>
    /// Gets the payment identifier.
    /// </summary>
    public Guid PaymentId { get; init; }
}