using Conflux.Payment.Application.Payments;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Conflux.Payment.Features.Payments.GetPayment;

/// <summary>
/// Provides the HTTP endpoint for retrieving payments.
/// </summary>
public static class GetPaymentEndpoint
{
    /// <summary>
    /// Maps the get payment endpoint.
    /// </summary>
    /// <param name="app">
    /// The application endpoint route builder.
    /// </param>
    public static void MapGetPaymentEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/api/v1/payments/{paymentId:guid}",
                HandleAsync)
            .WithName("GetPayment")
            .Produces<GetPaymentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<
        Results<
            Ok<GetPaymentResponse>,
            NotFound<string>>>
        HandleAsync(
            Guid paymentId,
            PaymentApplicationService paymentService,
            CancellationToken cancellationToken)
    {
        var result =
            await paymentService.GetAsync(
                paymentId,
                cancellationToken);

        return result.Status switch
        {
            GetPaymentResultStatus.Success =>
                TypedResults.Ok(
                    new GetPaymentResponse(
                        result.Payment!.Id,
                        result.Payment.OrderId,
                        result.Payment.CustomerId,
                        result.Payment.Amount,
                        result.Payment.Currency,
                        result.Payment.Status,
                        result.Payment.CreatedAt,
                        result.Payment.UpdatedAt)),

            GetPaymentResultStatus.NotFound =>
                TypedResults.NotFound(
                    result.Error ?? "Payment was not found."),

            _ =>
                throw new InvalidOperationException(
                    "Unsupported payment get result status.")
        };
    }
}