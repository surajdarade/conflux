using System.Text.Json;
using Conflux.Contracts.Events;
using Conflux.Outbox;
using Conflux.Observability;
using Conflux.Payment.Domain;
using Conflux.Payment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

using PaymentEntity = Conflux.Payment.Domain.Payment;

namespace Conflux.Payment.Application.Payments;

/// <summary>
/// Provides application operations for payments.
/// </summary>
public sealed class PaymentApplicationService
{
    private const string PaymentAuthorizedEventType =
        "payment.authorized.v1";

    private readonly PaymentDbContext _dbContext;
    private readonly ConfluxBusinessMetrics _metrics;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="PaymentApplicationService"/> class.
    /// </summary>
    /// <param name="dbContext">
    /// The Payment database context.
    /// </param>
    /// <param name="metrics"></param>
    public PaymentApplicationService(
        PaymentDbContext dbContext,
        ConfluxBusinessMetrics metrics)
    {
        _dbContext = dbContext;
        _metrics = metrics;
    }

    /// <summary>
    /// Initializes the service with default business metrics.
    /// </summary>
    /// <param name="dbContext">The payment database context.</param>
    public PaymentApplicationService(PaymentDbContext dbContext)
        : this(dbContext, new ConfluxBusinessMetrics())
    {
    }

    /// <summary>
    /// Authorizes a payment.
    /// </summary>
    /// <param name="orderId">
    /// The order associated with the payment.
    /// </param>
    /// <param name="customerId">
    /// The customer associated with the payment.
    /// </param>
    /// <param name="idempotencyKey">
    /// The idempotency key for the authorization request.
    /// </param>
    /// <param name="amount">
    /// The payment amount.
    /// </param>
    /// <param name="currency">
    /// The payment currency.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    /// <returns>
    /// The authorization result.
    /// </returns>
    public async Task<AuthorizePaymentResult> AuthorizeAsync(
        Guid orderId,
        Guid customerId,
        string? idempotencyKey,
        decimal amount,
        string? currency,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
        {
            return AuthorizePaymentResult.Invalid(
                "OrderId is required.");
        }

        if (customerId == Guid.Empty)
        {
            return AuthorizePaymentResult.Invalid(
                "CustomerId is required.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return AuthorizePaymentResult.Invalid(
                "Idempotency-Key is required.");
        }

        if (amount <= 0)
        {
            return AuthorizePaymentResult.Invalid(
                "Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            return AuthorizePaymentResult.Invalid(
                "Currency is required.");
        }

        var normalizedIdempotencyKey =
            idempotencyKey.Trim();

        var normalizedCurrency =
            currency.Trim().ToUpperInvariant();

        var existingPayment =
            await _dbContext.Payments
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    payment =>
                        payment.IdempotencyKey ==
                        normalizedIdempotencyKey,
                    cancellationToken);

        if (existingPayment is not null)
        {
            if (existingPayment.OrderId != orderId ||
                existingPayment.CustomerId != customerId ||
                existingPayment.Amount != amount ||
                existingPayment.Currency != normalizedCurrency)
            {
                return AuthorizePaymentResult.Conflict(
                    "The Idempotency-Key has already been used " +
                    "with different payment details.");
            }

            if (existingPayment.Status ==
                PaymentStatus.Authorized)
            {
                _metrics.PaymentAuthorized();
                return AuthorizePaymentResult.Success(
                    existingPayment,
                    alreadyAuthorized: true);
            }

            return AuthorizePaymentResult.Conflict(
                $"Payment cannot be authorized because its " +
                $"current status is {existingPayment.Status}.");
        }

        var payment = new PaymentEntity(
            Guid.NewGuid(),
            orderId,
            customerId,
            normalizedIdempotencyKey,
            amount,
            normalizedCurrency);

        payment.Authorize();

        var occurredAt = DateTimeOffset.UtcNow;

        var integrationEvent =
            new PaymentAuthorized
            {
                EventId = Guid.NewGuid(),
                OccurredAt = occurredAt,
                CorrelationId = orderId,
                CausationId = null,
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                CustomerId = payment.CustomerId,
                Amount = payment.Amount,
                Currency = payment.Currency
            };

        var outboxMessage =
            new OutboxMessage(
                integrationEvent.EventId,
                occurredAt,
                PaymentAuthorizedEventType,
                JsonSerializer.Serialize(
                    integrationEvent),
                integrationEvent.CorrelationId,
                integrationEvent.CausationId);

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        _dbContext.Payments.Add(payment);
        _dbContext.OutboxMessages.Add(outboxMessage);

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(
                cancellationToken);

            var concurrentPayment =
                await _dbContext.Payments
                    .AsNoTracking()
                    .SingleAsync(
                        existing =>
                            existing.IdempotencyKey ==
                            normalizedIdempotencyKey,
                        cancellationToken);

            if (concurrentPayment.OrderId != orderId ||
                concurrentPayment.CustomerId != customerId ||
                concurrentPayment.Amount != amount ||
                concurrentPayment.Currency != normalizedCurrency)
            {
                return AuthorizePaymentResult.Conflict(
                    "The Idempotency-Key has already been used " +
                    "with different payment details.");
            }

            if (concurrentPayment.Status ==
                PaymentStatus.Authorized)
            {
                return AuthorizePaymentResult.Success(
                    concurrentPayment,
                    alreadyAuthorized: true);
            }

            return AuthorizePaymentResult.Conflict(
                $"Payment cannot be authorized because its " +
                $"current status is {concurrentPayment.Status}.");
        }

        return AuthorizePaymentResult.Success(
            payment,
            alreadyAuthorized: false);
    }

    /// <summary>
    /// Captures an authorized payment.
    /// </summary>
    /// <param name="paymentId">
    /// The payment identifier.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    /// <returns>
    /// The capture result.
    /// </returns>
    public async Task<CapturePaymentResult> CaptureAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        if (paymentId == Guid.Empty)
        {
            return CapturePaymentResult.Invalid(
                "PaymentId is required.");
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var payment = await _dbContext.Payments
            .FromSqlInterpolated(
                $"SELECT * FROM payments WHERE \"Id\" = {paymentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CapturePaymentResult.NotFound(
                "Payment was not found.");
        }

        if (payment.Status == PaymentStatus.Captured)
        {
            await transaction.CommitAsync(cancellationToken);
            _metrics.PaymentCaptured();
        return CapturePaymentResult.Success(
                payment,
                alreadyCaptured: true);
        }

        if (payment.Status != PaymentStatus.Authorized)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CapturePaymentResult.Conflict(
                $"Payment cannot be captured because its current status is {payment.Status}.");
        }

        payment.Capture();
        var occurredAt = DateTimeOffset.UtcNow;
        var integrationEvent = new PaymentCaptured
        {
            EventId = Guid.NewGuid(),
            OccurredAt = occurredAt,
            CorrelationId = payment.OrderId,
            CausationId = null,
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            CustomerId = payment.CustomerId,
            Amount = payment.Amount,
            Currency = payment.Currency
        };

        _dbContext.OutboxMessages.Add(new OutboxMessage(
            integrationEvent.EventId,
            occurredAt,
            "payment.captured.v1",
            JsonSerializer.Serialize(integrationEvent),
            integrationEvent.CorrelationId,
            integrationEvent.CausationId));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CapturePaymentResult.Success(
            payment,
            alreadyCaptured: false);
    }

    /// <summary>
    /// Voids an authorized payment.
    /// </summary>
    /// <param name="paymentId">
    /// The payment identifier.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    /// <returns>
    /// The void result.
    /// </returns>
    public async Task<VoidPaymentResult> VoidAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        if (paymentId == Guid.Empty)
        {
            return VoidPaymentResult.Invalid(
                "PaymentId is required.");
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var payment = await _dbContext.Payments
            .FromSqlInterpolated(
                $"SELECT * FROM payments WHERE \"Id\" = {paymentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return VoidPaymentResult.NotFound(
                "Payment was not found.");
        }

        if (payment.Status == PaymentStatus.Voided)
        {
            await transaction.CommitAsync(cancellationToken);
            _metrics.PaymentVoided();
        return VoidPaymentResult.Success(
                payment,
                alreadyVoided: true);
        }

        if (payment.Status != PaymentStatus.Authorized)
        {
            await transaction.RollbackAsync(cancellationToken);
            return VoidPaymentResult.Conflict(
                $"Payment cannot be voided because its current status is {payment.Status}.");
        }

        payment.Void();
        var occurredAt = DateTimeOffset.UtcNow;
        var integrationEvent = new PaymentVoided
        {
            EventId = Guid.NewGuid(),
            OccurredAt = occurredAt,
            CorrelationId = payment.OrderId,
            CausationId = null,
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            CustomerId = payment.CustomerId
        };

        _dbContext.OutboxMessages.Add(new OutboxMessage(
            integrationEvent.EventId,
            occurredAt,
            "payment.voided.v1",
            JsonSerializer.Serialize(integrationEvent),
            integrationEvent.CorrelationId,
            integrationEvent.CausationId));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return VoidPaymentResult.Success(
            payment,
            alreadyVoided: false);
    }

    /// <summary>
    /// Refunds a previously captured payment.
    /// </summary>
    /// <param name="paymentId">The payment identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The refund operation result.</returns>
    public async Task<RefundPaymentResult> RefundAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        if (paymentId == Guid.Empty)
        {
            return RefundPaymentResult.Invalid("PaymentId is required.");
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var payment = await _dbContext.Payments
            .FromSqlInterpolated(
                $"SELECT * FROM payments WHERE \"Id\" = {paymentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return RefundPaymentResult.NotFound("Payment was not found.");
        }

        if (payment.Status == PaymentStatus.Refunded)
        {
            await transaction.CommitAsync(cancellationToken);
            _metrics.PaymentRefunded();
        return RefundPaymentResult.Success(payment, alreadyRefunded: true);
        }

        if (payment.Status != PaymentStatus.Captured)
        {
            await transaction.RollbackAsync(cancellationToken);
            return RefundPaymentResult.Conflict(
                $"Payment cannot be refunded because its current status is {payment.Status}.");
        }

        payment.Refund();

        var occurredAt = DateTimeOffset.UtcNow;
        var integrationEvent = new PaymentRefunded
        {
            EventId = Guid.NewGuid(),
            OccurredAt = occurredAt,
            CorrelationId = payment.OrderId,
            CausationId = null,
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            CustomerId = payment.CustomerId,
            Amount = payment.Amount,
            Currency = payment.Currency
        };

        _dbContext.OutboxMessages.Add(new OutboxMessage(
            integrationEvent.EventId,
            occurredAt,
            "payment.refunded.v1",
            JsonSerializer.Serialize(integrationEvent),
            integrationEvent.CorrelationId,
            integrationEvent.CausationId));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return RefundPaymentResult.Success(payment, alreadyRefunded: false);
    }

    /// <summary>
    /// Gets a payment by its identifier.
    /// </summary>
    /// <param name="paymentId">
    /// The payment identifier.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    /// <returns>
    /// The payment lookup result.
    /// </returns>
    public async Task<GetPaymentResult> GetAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        if (paymentId == Guid.Empty)
        {
            return GetPaymentResult.NotFound(
                "Payment was not found.");
        }

        var payment =
            await _dbContext.Payments
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    currentPayment =>
                        currentPayment.Id == paymentId,
                    cancellationToken);

        if (payment is null)
        {
            return GetPaymentResult.NotFound(
                "Payment was not found.");
        }

        return GetPaymentResult.Success(payment);
    }

    private static bool IsUniqueViolation(
        DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            };
    }
}

/// <summary>
/// Represents the possible results of a payment authorization operation.
/// </summary>
public sealed record AuthorizePaymentResult
{
    private AuthorizePaymentResult(
        AuthorizePaymentResultStatus status,
        PaymentEntity? payment,
        bool alreadyAuthorized,
        string? error)
    {
        Status = status;
        Payment = payment;
        AlreadyAuthorized = alreadyAuthorized;
        Error = error;
    }

    /// <summary>
    /// Gets the result status.
    /// </summary>
    public AuthorizePaymentResultStatus Status { get; }

    /// <summary>
    /// Gets the payment when the operation succeeded.
    /// </summary>
    public PaymentEntity? Payment { get; }

    /// <summary>
    /// Gets a value indicating whether the payment was already authorized.
    /// </summary>
    public bool AlreadyAuthorized { get; }

    /// <summary>
    /// Gets the error message when the operation did not succeed.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Creates a successful authorization result.
    /// </summary>
    public static AuthorizePaymentResult Success(
        PaymentEntity payment,
        bool alreadyAuthorized)
    {
        return new AuthorizePaymentResult(
            AuthorizePaymentResultStatus.Success,
            payment,
            alreadyAuthorized,
            null);
    }

    /// <summary>
    /// Creates an invalid authorization result.
    /// </summary>
    public static AuthorizePaymentResult Invalid(
        string error)
    {
        return new AuthorizePaymentResult(
            AuthorizePaymentResultStatus.Invalid,
            null,
            false,
            error);
    }

    /// <summary>
    /// Creates a conflict authorization result.
    /// </summary>
    public static AuthorizePaymentResult Conflict(
        string error)
    {
        return new AuthorizePaymentResult(
            AuthorizePaymentResultStatus.Conflict,
            null,
            false,
            error);
    }
}

/// <summary>
/// Represents the status of a payment authorization operation.
/// </summary>
public enum AuthorizePaymentResultStatus
{
    /// <summary>
    /// The payment was authorized successfully.
    /// </summary>
    Success,

    /// <summary>
    /// The authorization request was invalid.
    /// </summary>
    Invalid,

    /// <summary>
    /// The authorization request conflicts with the current payment state.
    /// </summary>
    Conflict
}

/// <summary>
/// Represents the possible results of a payment capture operation.
/// </summary>
public sealed record CapturePaymentResult
{
    private CapturePaymentResult(
        CapturePaymentResultStatus status,
        PaymentEntity? payment,
        bool alreadyCaptured,
        string? error)
    {
        Status = status;
        Payment = payment;
        AlreadyCaptured = alreadyCaptured;
        Error = error;
    }

    /// <summary>
    /// Gets the result status.
    /// </summary>
    public CapturePaymentResultStatus Status { get; }

    /// <summary>
    /// Gets the payment when the operation succeeded.
    /// </summary>
    public PaymentEntity? Payment { get; }

    /// <summary>
    /// Gets a value indicating whether the payment was already captured.
    /// </summary>
    public bool AlreadyCaptured { get; }

    /// <summary>
    /// Gets the error message when the operation did not succeed.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Creates a successful capture result.
    /// </summary>
    public static CapturePaymentResult Success(
        PaymentEntity payment,
        bool alreadyCaptured)
    {
        return new CapturePaymentResult(
            CapturePaymentResultStatus.Success,
            payment,
            alreadyCaptured,
            null);
    }

    /// <summary>
    /// Creates an invalid capture result.
    /// </summary>
    public static CapturePaymentResult Invalid(
        string error)
    {
        return new CapturePaymentResult(
            CapturePaymentResultStatus.Invalid,
            null,
            false,
            error);
    }

    /// <summary>
    /// Creates a not-found capture result.
    /// </summary>
    public static CapturePaymentResult NotFound(
        string error)
    {
        return new CapturePaymentResult(
            CapturePaymentResultStatus.NotFound,
            null,
            false,
            error);
    }

    /// <summary>
    /// Creates a conflict capture result.
    /// </summary>
    public static CapturePaymentResult Conflict(
        string error)
    {
        return new CapturePaymentResult(
            CapturePaymentResultStatus.Conflict,
            null,
            false,
            error);
    }
}

/// <summary>
/// Represents the status of a payment capture operation.
/// </summary>
public enum CapturePaymentResultStatus
{
    /// <summary>
    /// The payment was captured successfully.
    /// </summary>
    Success,

    /// <summary>
    /// The capture request was invalid.
    /// </summary>
    Invalid,

    /// <summary>
    /// The payment does not exist.
    /// </summary>
    NotFound,

    /// <summary>
    /// The payment cannot be captured in its current state.
    /// </summary>
    Conflict
}

/// <summary>
/// Represents the possible results of a payment void operation.
/// </summary>
public sealed record VoidPaymentResult
{
    private VoidPaymentResult(
        VoidPaymentResultStatus status,
        PaymentEntity? payment,
        bool alreadyVoided,
        string? error)
    {
        Status = status;
        Payment = payment;
        AlreadyVoided = alreadyVoided;
        Error = error;
    }

    /// <summary>
    /// Gets the result status.
    /// </summary>
    public VoidPaymentResultStatus Status { get; }

    /// <summary>
    /// Gets the payment when the operation succeeded.
    /// </summary>
    public PaymentEntity? Payment { get; }

    /// <summary>
    /// Gets a value indicating whether the payment was already voided.
    /// </summary>
    public bool AlreadyVoided { get; }

    /// <summary>
    /// Gets the error message when the operation did not succeed.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Creates a successful void result.
    /// </summary>
    public static VoidPaymentResult Success(
        PaymentEntity payment,
        bool alreadyVoided)
    {
        return new VoidPaymentResult(
            VoidPaymentResultStatus.Success,
            payment,
            alreadyVoided,
            null);
    }

    /// <summary>
    /// Creates an invalid void result.
    /// </summary>
    public static VoidPaymentResult Invalid(
        string error)
    {
        return new VoidPaymentResult(
            VoidPaymentResultStatus.Invalid,
            null,
            false,
            error);
    }

    /// <summary>
    /// Creates a not-found void result.
    /// </summary>
    public static VoidPaymentResult NotFound(
        string error)
    {
        return new VoidPaymentResult(
            VoidPaymentResultStatus.NotFound,
            null,
            false,
            error);
    }

    /// <summary>
    /// Creates a conflict void result.
    /// </summary>
    public static VoidPaymentResult Conflict(
        string error)
    {
        return new VoidPaymentResult(
            VoidPaymentResultStatus.Conflict,
            null,
            false,
            error);
    }
}

/// <summary>
/// Represents the status of a payment void operation.
/// </summary>
public enum VoidPaymentResultStatus
{
    /// <summary>
    /// The payment was voided successfully.
    /// </summary>
    Success,

    /// <summary>
    /// The void request was invalid.
    /// </summary>
    Invalid,

    /// <summary>
    /// The payment does not exist.
    /// </summary>
    NotFound,

    /// <summary>
    /// The payment cannot be voided in its current state.
    /// </summary>
    Conflict
}

/// <summary>
/// Represents the possible results of a payment refund operation.
/// </summary>
public sealed record RefundPaymentResult
{
    private RefundPaymentResult(
        RefundPaymentResultStatus status,
        PaymentEntity? payment,
        string? error,
        bool alreadyRefunded)
    {
        Status = status;
        Payment = payment;
        Error = error;
        AlreadyRefunded = alreadyRefunded;
    }

    /// <summary>Gets the operation status.</summary>
    public RefundPaymentResultStatus Status { get; }

    /// <summary>Gets the payment when the operation succeeds.</summary>
    public PaymentEntity? Payment { get; }

    /// <summary>Gets the error when the operation does not succeed.</summary>
    public string? Error { get; }

    /// <summary>Gets whether the payment was already refunded.</summary>
    public bool AlreadyRefunded { get; }

    /// <summary>Creates a successful result.</summary>
    public static RefundPaymentResult Success(
        PaymentEntity payment,
        bool alreadyRefunded) =>
        new(
            RefundPaymentResultStatus.Success,
            payment,
            null,
            alreadyRefunded);

    /// <summary>Creates an invalid-input result.</summary>
    public static RefundPaymentResult Invalid(string error) =>
        new(RefundPaymentResultStatus.Invalid, null, error, false);

    /// <summary>Creates a not-found result.</summary>
    public static RefundPaymentResult NotFound(string error) =>
        new(RefundPaymentResultStatus.NotFound, null, error, false);

    /// <summary>Creates a conflict result.</summary>
    public static RefundPaymentResult Conflict(string error) =>
        new(RefundPaymentResultStatus.Conflict, null, error, false);
}

/// <summary>
/// Represents the possible statuses of a payment refund operation.
/// </summary>
public enum RefundPaymentResultStatus
{
    /// <summary>The refund succeeded.</summary>
    Success,

    /// <summary>The request was invalid.</summary>
    Invalid,

    /// <summary>The payment was not found.</summary>
    NotFound,

    /// <summary>The current payment state conflicts with the requested operation.</summary>
    Conflict
}

/// <summary>
/// Represents the possible results of a payment lookup operation.
/// </summary>
public sealed record GetPaymentResult
{
    private GetPaymentResult(
        GetPaymentResultStatus status,
        PaymentEntity? payment,
        string? error)
    {
        Status = status;
        Payment = payment;
        Error = error;
    }

    /// <summary>
    /// Gets the result status.
    /// </summary>
    public GetPaymentResultStatus Status { get; }

    /// <summary>
    /// Gets the payment when the operation succeeded.
    /// </summary>
    public PaymentEntity? Payment { get; }

    /// <summary>
    /// Gets the error message when the operation did not succeed.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Creates a successful payment lookup result.
    /// </summary>
    /// <param name="payment">
    /// The payment.
    /// </param>
    /// <returns>
    /// A successful result.
    /// </returns>
    public static GetPaymentResult Success(
        PaymentEntity payment)
    {
        return new GetPaymentResult(
            GetPaymentResultStatus.Success,
            payment,
            null);
    }

    /// <summary>
    /// Creates a not-found payment lookup result.
    /// </summary>
    /// <param name="error">
    /// The error message.
    /// </param>
    /// <returns>
    /// A not-found result.
    /// </returns>
    public static GetPaymentResult NotFound(
        string error)
    {
        return new GetPaymentResult(
            GetPaymentResultStatus.NotFound,
            null,
            error);
    }
}

/// <summary>
/// Represents the status of a payment lookup operation.
/// </summary>
public enum GetPaymentResultStatus
{
    /// <summary>
    /// The payment was found.
    /// </summary>
    Success,

    /// <summary>
    /// The payment was not found.
    /// </summary>
    NotFound
}