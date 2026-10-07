using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Conflux.Fulfillment.Consumer;
using Conflux.Fulfillment.Infrastructure;
using Conflux.Kafka;
using Conflux.Observability;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddConfluxObservability("conflux-fulfillment");
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<FulfillmentDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("FulfillmentDatabase")));
builder.Services.AddOptions<FulfillmentConsumerOptions>().Bind(builder.Configuration.GetSection("Kafka"));
builder.Services.AddOptions<KafkaRetryOptions>().Bind(builder.Configuration.GetSection(KafkaRetryOptions.SectionName));
builder.Services.AddSingleton<KafkaRetryPublisher>();
builder.Services.AddHostedService<KafkaRetryWorker>();
builder.Services.AddHostedService<KafkaDlqRecoveryWorker>();
builder.Services.AddHostedService<FulfillmentConsumer>();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().Database.MigrateAsync();
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
app.MapGet("/api/v1/fulfillment/{orderId:guid}", async (Guid orderId, FulfillmentDbContext db, CancellationToken ct) =>
{
    var fulfillment = await db.Fulfillments.AsNoTracking().SingleOrDefaultAsync(item => item.OrderId == orderId, ct);
    return fulfillment is null ? Results.NotFound() : Results.Ok(new
    {
        fulfillment.Id,
        fulfillment.OrderId,
        fulfillment.CustomerId,
        fulfillment.Status,
        fulfillment.CreatedAt,
        fulfillment.UpdatedAt
    });
});
app.Run();

/// <summary>Provides an accessible entry point type for integration tests.</summary>
public partial class Program
{
}
