using Conflux.Payment.Domain;

namespace Conflux.Payment.Features.Payments.GetPayment;

/// <summary>
/// Represents a payment returned by the Payment API.
/// </summary>
public sealed record GetPaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);