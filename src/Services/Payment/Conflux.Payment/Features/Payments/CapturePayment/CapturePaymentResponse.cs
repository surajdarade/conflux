using Conflux.Payment.Domain;

namespace Conflux.Payment.Features.Payments.CapturePayment;

/// <summary>
/// Represents the result of capturing a payment.
/// </summary>
public sealed record CapturePaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    bool AlreadyCaptured);