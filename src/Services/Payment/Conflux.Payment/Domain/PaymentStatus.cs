namespace Conflux.Payment.Domain;

/// <summary>
/// Represents the lifecycle state of a payment.
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// The payment has been created but has not yet been authorized.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The payment has been successfully authorized.
    /// </summary>
    Authorized = 1,

    /// <summary>
    /// Payment authorization failed.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// The authorized payment has been captured.
    /// </summary>
    Captured = 3,

    /// <summary>
    /// The payment authorization or capture has been voided.
    /// </summary>
    Voided = 4
}