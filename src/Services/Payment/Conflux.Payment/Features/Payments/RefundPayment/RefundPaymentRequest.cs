namespace Conflux.Payment.Features.Payments.RefundPayment;

/// <summary>
/// Represents a payment refund request.
/// </summary>
public sealed record RefundPaymentRequest
{
    /// <summary>Gets the payment identifier from the route.</summary>
    public Guid PaymentId { get; init; }
}
