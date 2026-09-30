namespace Conflux.Payment.Features.Payments.CapturePayment;

/// <summary>
/// Represents an HTTP request to capture an authorized payment.
/// </summary>
public sealed record CapturePaymentRequest
{
    /// <summary>
    /// Gets the payment identifier.
    /// </summary>
    public Guid PaymentId { get; init; }
}