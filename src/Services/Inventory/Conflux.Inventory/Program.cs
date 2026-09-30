using Conflux.Inventory.Application.Inventory;
using Conflux.Inventory.Features.Inventory.CreateInventory;
using Conflux.Inventory.Features.Inventory.GetInventory;
using Conflux.Inventory.Features.Inventory.ReleaseInventory;
using Conflux.Inventory.Features.Inventory.ReserveInventory;
using Conflux.Inventory.Grpc;
using Conflux.Inventory.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString(
            "InventoryDatabase")));

builder.Services.AddScoped<InventoryApplicationService>();

builder.Services.AddGrpc();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapCreateInventoryEndpoint();
app.MapGetInventoryEndpoint();
app.MapReserveInventoryEndpoint();
app.MapReleaseInventoryEndpoint();

app.MapGrpcService<InventoryGrpcService>();

app.Run();

/// <summary>
/// Provides an accessible entry point type for ASP.NET Core integration tests.
/// </summary>
public partial class Program
{
}