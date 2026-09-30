using Conflux.Payment.Application.Payments;

namespace Conflux.Payment.Features.Payments.AuthorizePayment;

/// <summary>
/// Provides the HTTP endpoint for payment authorization.
/// </summary>
public static class AuthorizePaymentEndpoint
{
    /// <summary>
    /// Maps the payment authorization endpoint.
    /// </summary>
    /// <param name="app">
    /// The endpoint route builder.
    /// </param>
    public static void MapAuthorizePaymentEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/v1/payments/authorize",
            async (
                AuthorizePaymentRequest request,
                HttpRequest httpRequest,
                PaymentApplicationService paymentService,
                CancellationToken cancellationToken) =>
            {
                var idempotencyKey =
                    httpRequest.Headers["Idempotency-Key"]
                        .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.BadRequest(
                        new
                        {
                            Error =
                                "The Idempotency-Key header is required."
                        });
                }

                var result =
                    await paymentService.AuthorizeAsync(
                        request.OrderId,
                        request.CustomerId,
                        idempotencyKey,
                        request.Amount,
                        request.Currency,
                        cancellationToken);

                return result.Status switch
                {
                    AuthorizePaymentResultStatus.Success
                        when result.Payment is not null =>
                        result.AlreadyAuthorized
                            ? Results.Ok(
                                ToResponse(result))
                            : Results.Created(
                                $"/api/v1/payments/{result.Payment.Id}",
                                ToResponse(result)),

                    AuthorizePaymentResultStatus.Invalid =>
                        Results.BadRequest(
                            new
                            {
                                Error = result.Error
                            }),

                    AuthorizePaymentResultStatus.Conflict =>
                        Results.Conflict(
                            new
                            {
                                Error = result.Error
                            }),

                    _ =>
                        Results.Problem(
                            statusCode:
                                StatusCodes.Status500InternalServerError,
                            detail:
                                "Unsupported payment authorization result.")
                };
            });
    }

    private static AuthorizePaymentResponse ToResponse(
        AuthorizePaymentResult result)
    {
        var payment = result.Payment
            ?? throw new InvalidOperationException(
                "A successful payment result must contain a payment.");

        return new AuthorizePaymentResponse
        {
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            CustomerId = payment.CustomerId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status,
            AlreadyAuthorized = result.AlreadyAuthorized
        };
    }
}