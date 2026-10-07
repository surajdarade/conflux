using Conflux.Payment.Application.Payments;

namespace Conflux.Payment.Features.Payments.RefundPayment;

/// <summary>
/// Provides the HTTP endpoint for payment refunds.
/// </summary>
public static class RefundPaymentEndpoint
{
    /// <summary>
    /// Maps the payment refund endpoint.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    public static void MapRefundPaymentEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/v1/payments/{paymentId:guid}/refund",
            async (
                Guid paymentId,
                PaymentApplicationService paymentService,
                CancellationToken cancellationToken) =>
            {
                var result = await paymentService.RefundAsync(
                    paymentId,
                    cancellationToken);

                return result.Status switch
                {
                    RefundPaymentResultStatus.Success
                        when result.Payment is not null =>
                        Results.Ok(
                            new RefundPaymentResponse
                            {
                                PaymentId = result.Payment.Id,
                                OrderId = result.Payment.OrderId,
                                Amount = result.Payment.Amount,
                                Currency = result.Payment.Currency,
                                Status = result.Payment.Status,
                                AlreadyRefunded = result.AlreadyRefunded
                            }),

                    RefundPaymentResultStatus.Invalid =>
                        Results.BadRequest(new { Error = result.Error }),

                    RefundPaymentResultStatus.NotFound =>
                        Results.NotFound(new { Error = result.Error }),

                    RefundPaymentResultStatus.Conflict =>
                        Results.Conflict(new { Error = result.Error }),

                    _ =>
                        Results.Problem(
                            statusCode: StatusCodes.Status500InternalServerError,
                            detail: "Unsupported payment refund result.")
                };
            });
    }
}
