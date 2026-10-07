using Conflux.Payment.Domain;

namespace Conflux.Payment.Features.Payments.RefundPayment;

/// <summary>
/// Represents a successful payment refund response.
/// </summary>
public sealed record RefundPaymentResponse
{
    /// <summary>Gets the payment identifier.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>Gets the order identifier.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>Gets the refunded amount.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Gets the payment currency.</summary>
    public required string Currency { get; init; }

    /// <summary>Gets the resulting payment status.</summary>
    public required PaymentStatus Status { get; init; }

    /// <summary>Gets whether the payment was already refunded.</summary>
    public required bool AlreadyRefunded { get; init; }
}
