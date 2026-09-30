using Conflux.Payment.Application.Payments;
using Conflux.Payment.Features.Payments.AuthorizePayment;
using Conflux.Payment.Features.Payments.CapturePayment;
using Conflux.Payment.Features.Payments.VoidPayment;
using Conflux.Payment.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

builder.Services.AddDbContext<PaymentDbContext>(
    options =>
        options.UseNpgsql(
            builder.Configuration.GetConnectionString(
                "PaymentDatabase")));

builder.Services.AddScoped<PaymentApplicationService>();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapAuthorizePaymentEndpoint();

app.MapCapturePaymentEndpoint();

app.MapVoidPaymentEndpoint();

app.Run();

/// <summary>
/// Provides an accessible entry point type for ASP.NET Core integration tests.
/// </summary>
public partial class Program
{
}