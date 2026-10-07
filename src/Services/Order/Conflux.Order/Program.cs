using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Conflux.Observability;
using Conflux.Contracts.Inventory;
using Conflux.Order.Application.Orders;
using Conflux.Order.Clients.Inventory;
using Conflux.Order.Features.Orders.CreateOrder;
using Conflux.Order.Features.Orders.GetOrder;
using Conflux.Order.Infrastructure;
using Conflux.Order.Saga;
using Microsoft.Extensions.Options;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Conflux.Kafka;

var builder = WebApplication.CreateBuilder(args);
builder.AddConfluxObservability("conflux-order");

builder.Services.AddHealthChecks();

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString(
            "OrderDatabase")));

var inventoryAddress =
    builder.Configuration["services:inventory:http:0"]
    ?? builder.Configuration.GetConnectionString("InventoryGrpc")
    ?? builder.Configuration["Services:Inventory:GrpcAddress"];

if (string.IsNullOrWhiteSpace(inventoryAddress))
{
    throw new InvalidOperationException(
        "The Inventory gRPC address is not configured.");
}

builder.Services.AddSingleton(
    GrpcChannel.ForAddress(inventoryAddress));

builder.Services.AddSingleton<InventoryService.InventoryServiceClient>(
    serviceProvider =>
    {
        var channel =
            serviceProvider.GetRequiredService<GrpcChannel>();

        return new InventoryService.InventoryServiceClient(
            channel);
    });

builder.Services.AddScoped<IInventoryClient, InventoryGrpcClient>();

builder.Services.AddScoped<OrderInventoryOrchestrator>();

builder.Services.AddHttpClient(
    "Payment",
    client =>
    {
        var paymentAddress =
            builder.Configuration["services:payment:http:0"]
            ?? builder.Configuration["Services:Payment:BaseAddress"]
            ?? "http://localhost:5150";

        client.BaseAddress = new Uri(paymentAddress);
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.Retry.MaxRetryAttempts = 3;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    });

builder.Services
    .AddOptions<KafkaRetryOptions>()
    .Bind(builder.Configuration.GetSection(KafkaRetryOptions.SectionName));
builder.Services.AddSingleton<KafkaRetryPublisher>();
builder.Services.AddHostedService<KafkaRetryWorker>();
builder.Services.AddHostedService<KafkaDlqRecoveryWorker>();

builder.Services
    .AddOptions<CheckoutSagaOptions>()
    .Bind(builder.Configuration.GetSection("CheckoutSaga"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.BootstrapServers), "CheckoutSaga Kafka bootstrap servers are required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.OrderEventsTopic), "CheckoutSaga Order topic is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.PaymentEventsTopic), "CheckoutSaga Payment topic is required.")
    .ValidateOnStart();

if (builder.Configuration.GetValue<bool?>("CheckoutSaga:Enabled") ?? true)
{
    builder.Services.AddHostedService<CheckoutSagaWorker>();
}

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var migrationDbContext = migrationScope.ServiceProvider.GetRequiredService<OrderDbContext>();
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

app.MapCreateOrderEndpoint();
app.MapGetOrderEndpoint();

app.Run();

/// <summary>
/// Provides an accessible entry point type for ASP.NET Core integration tests.
/// </summary>
public partial class Program
{
}