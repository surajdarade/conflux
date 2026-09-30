using Conflux.Payment.Domain;

namespace Conflux.Payment.Features.Payments.VoidPayment;

/// <summary>
/// Represents the result of voiding a payment.
/// </summary>
public sealed record VoidPaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    bool AlreadyVoided);