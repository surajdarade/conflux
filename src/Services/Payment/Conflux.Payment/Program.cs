using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Conflux.Observability;
using Conflux.Payment.Application.Payments;
using Conflux.Payment.Features.Payments.AuthorizePayment;
using Conflux.Payment.Features.Payments.CapturePayment;
using Conflux.Payment.Features.Payments.GetPayment;
using Conflux.Payment.Features.Payments.RefundPayment;
using Conflux.Payment.Features.Payments.VoidPayment;
using Conflux.Payment.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddConfluxObservability("conflux-payment");

builder.Services.AddHealthChecks();

builder.Services.AddDbContext<PaymentDbContext>(
    options =>
        options.UseNpgsql(
            builder.Configuration.GetConnectionString(
                "PaymentDatabase")));

builder.Services.AddScoped<PaymentApplicationService>();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var migrationDbContext = migrationScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await migrationDbContext.Database.MigrateAsync();
}

app.MapHealthChecks(
    "/alive",
    new HealthCheckOptions
    {
        Predicate = static _ => false
    });

app.MapHealthChecks("/ready");
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();

app.MapAuthorizePaymentEndpoint();

app.MapCapturePaymentEndpoint();

app.MapVoidPaymentEndpoint();

app.MapGetPaymentEndpoint();
app.MapRefundPaymentEndpoint();

app.Run();

/// <summary>
/// Provides an accessible entry point type for ASP.NET Core integration tests.
/// </summary>
public partial class Program
{
}