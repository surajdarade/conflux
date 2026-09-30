using Conflux.Payment.Application.Payments;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Conflux.Payment.Features.Payments.VoidPayment;

/// <summary>
/// Provides the HTTP endpoint for voiding payments.
/// </summary>
public static class VoidPaymentEndpoint
{
    /// <summary>
    /// Maps the void payment endpoint.
    /// </summary>
    /// <param name="app">
    /// The application endpoint route builder.
    /// </param>
    public static void MapVoidPaymentEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/api/v1/payments/{paymentId:guid}/void",
                HandleAsync)
            .WithName("VoidPayment")
            .Produces<VoidPaymentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    private static async Task<
        Results<
            Ok<VoidPaymentResponse>,
            BadRequest<string>,
            NotFound<string>,
            Conflict<string>>>
        HandleAsync(
            Guid paymentId,
            PaymentApplicationService paymentService,
            CancellationToken cancellationToken)
    {
        var result =
            await paymentService.VoidAsync(
                paymentId,
                cancellationToken);

        return result.Status switch
        {
            VoidPaymentResultStatus.Success =>
                TypedResults.Ok(
                    new VoidPaymentResponse(
                        result.Payment!.Id,
                        result.Payment.OrderId,
                        result.Payment.CustomerId,
                        result.Payment.Amount,
                        result.Payment.Currency,
                        result.Payment.Status,
                        result.AlreadyVoided)),

            VoidPaymentResultStatus.Invalid =>
                TypedResults.BadRequest(
                    result.Error ?? "Invalid void request."),

            VoidPaymentResultStatus.NotFound =>
                TypedResults.NotFound(
                    result.Error ?? "Payment was not found."),

            VoidPaymentResultStatus.Conflict =>
                TypedResults.Conflict(
                    result.Error ?? "Payment cannot be voided."),

            _ =>
                throw new InvalidOperationException(
                    "Unsupported payment void result status.")
        };
    }
}