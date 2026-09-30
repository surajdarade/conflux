extern alias Order;

using Conflux.Contracts.Inventory;
using Conflux.Order.Clients.Inventory;
using Conflux.Order.Infrastructure;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using OrderProgram = Order::Program;

namespace Conflux.Order.IntegrationTests.Infrastructure;

/// <summary>
/// Provides a PostgreSQL-backed ASP.NET Core test host for Order integration tests
/// and connects the Order service to a real Inventory test host through gRPC.
/// </summary>
public sealed class OrderApiFactory :
    WebApplicationFactory<OrderProgram>
{
    private readonly PostgreSqlContainer _postgresContainer;
    private readonly InventoryGrpcTestFactory _inventoryFactory;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="OrderApiFactory"/> class.
    /// </summary>
    /// <param name="postgresContainer">
    /// The PostgreSQL container used by the Order test host.
    /// </param>
    /// <param name="inventoryFactory">
    /// The Inventory test host used by the Order gRPC client.
    /// </param>
    public OrderApiFactory(
        PostgreSqlContainer postgresContainer,
        InventoryGrpcTestFactory inventoryFactory)
    {
        _postgresContainer = postgresContainer;
        _inventoryFactory = inventoryFactory;
    }

    /// <summary>
    /// Configures the ASP.NET Core host used by the integration tests.
    /// </summary>
    /// <param name="builder">
    /// The web host builder being configured.
    /// </param>
    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseSetting(
            "ConnectionStrings:OrderDatabase",
            _postgresContainer.GetConnectionString());

        builder.UseEnvironment("Development");

        builder.ConfigureServices(
            services =>
            {
                services.RemoveAll<
                    InventoryService.InventoryServiceClient>();

                services.RemoveAll<GrpcChannel>();

                services.RemoveAll<IInventoryClient>();

                var channel =
                    _inventoryFactory.CreateGrpcChannel();

                services.AddSingleton(channel);

                services.AddSingleton(
                    new InventoryService.InventoryServiceClient(
                        channel));

                services.AddScoped<IInventoryClient>(
                    serviceProvider =>
                    {
                        var client =
                            serviceProvider
                                .GetRequiredService<
                                    InventoryService
                                        .InventoryServiceClient>();

                        return new InventoryGrpcClient(client);
                    });
            });
    }

    /// <summary>
    /// Applies the Order database migrations to the test database.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token used to cancel the migration operation.
    /// </param>
    public async Task ApplyDatabaseMigrationsAsync(
        CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<OrderDbContext>();

        await dbContext.Database.MigrateAsync(
            cancellationToken);
    }
}