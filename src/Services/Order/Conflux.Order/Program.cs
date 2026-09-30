using Conflux.Contracts.Inventory;
using Conflux.Order.Clients.Inventory;
using Conflux.Order.Features.Orders.CreateOrder;
using Conflux.Order.Features.Orders.GetOrder;
using Conflux.Order.Infrastructure;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString(
            "OrderDatabase")));

var inventoryAddress =
    builder.Configuration.GetConnectionString(
        "InventoryGrpc")
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

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapCreateOrderEndpoint();
app.MapGetOrderEndpoint();

app.Run();

/// <summary>
/// Provides an accessible entry point type for ASP.NET Core integration tests.
/// </summary>
public partial class Program
{
}