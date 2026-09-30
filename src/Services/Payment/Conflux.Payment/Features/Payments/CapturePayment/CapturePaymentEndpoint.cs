using Conflux.Payment.Application.Payments;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Conflux.Payment.Features.Payments.CapturePayment;

/// <summary>
/// Provides the HTTP endpoint for capturing payments.
/// </summary>
public static class CapturePaymentEndpoint
{
    /// <summary>
    /// Maps the capture payment endpoint.
    /// </summary>
    /// <param name="app">
    /// The application endpoint route builder.
    /// </param>
    public static void MapCapturePaymentEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/api/v1/payments/{paymentId:guid}/capture",
                HandleAsync)
            .WithName("CapturePayment")
            .Produces<CapturePaymentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    private static async Task<
        Results<
            Ok<CapturePaymentResponse>,
            BadRequest<string>,
            NotFound<string>,
            Conflict<string>>>
        HandleAsync(
            Guid paymentId,
            PaymentApplicationService paymentService,
            CancellationToken cancellationToken)
    {
        var result =
            await paymentService.CaptureAsync(
                paymentId,
                cancellationToken);

        return result.Status switch
        {
            CapturePaymentResultStatus.Success =>
                TypedResults.Ok(
                    new CapturePaymentResponse(
                        result.Payment!.Id,
                        result.Payment.OrderId,
                        result.Payment.CustomerId,
                        result.Payment.Amount,
                        result.Payment.Currency,
                        result.Payment.Status,
                        result.AlreadyCaptured)),

            CapturePaymentResultStatus.Invalid =>
                TypedResults.BadRequest(
                    result.Error ?? "Invalid capture request."),

            CapturePaymentResultStatus.NotFound =>
                TypedResults.NotFound(
                    result.Error ?? "Payment was not found."),

            CapturePaymentResultStatus.Conflict =>
                TypedResults.Conflict(
                    result.Error ?? "Payment cannot be captured."),

            _ =>
                throw new InvalidOperationException(
                    "Unsupported payment capture result status.")
        };
    }
}