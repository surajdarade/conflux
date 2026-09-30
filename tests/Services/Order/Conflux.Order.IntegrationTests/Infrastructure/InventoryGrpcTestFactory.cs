extern alias Inventory;

using Conflux.Inventory.Infrastructure;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using InventoryProgram = Inventory::Program;

namespace Conflux.Order.IntegrationTests.Infrastructure;

/// <summary>
/// Provides a PostgreSQL-backed ASP.NET Core test host for the Inventory service
/// and exposes a gRPC channel backed by the test server's HTTP handler.
/// </summary>
public sealed class InventoryGrpcTestFactory :
    WebApplicationFactory<InventoryProgram> {
    private readonly PostgreSqlContainer _postgresContainer =
        new PostgreSqlBuilder("postgres:17")
            .WithDatabase("conflux_inventory_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    /// <summary>
    /// Starts the PostgreSQL container and initializes the Inventory test host.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token used to cancel startup.
    /// </param>
    public async Task StartAsync(
        CancellationToken cancellationToken) {
        await _postgresContainer.StartAsync(
            cancellationToken);

        _ = CreateClient();

        using var scope = Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<InventoryDbContext>();

        await dbContext.Database.MigrateAsync(
            cancellationToken);
    }

    /// <summary>
    /// Creates a gRPC channel connected to the Inventory test server.
    /// </summary>
    /// <returns>
    /// A gRPC channel configured to use the Inventory test server.
    /// </returns>
    public GrpcChannel CreateGrpcChannel() {
        var handler = Server.CreateHandler();

        return GrpcChannel.ForAddress(
            "http://localhost",
            new GrpcChannelOptions
            {
                HttpHandler = handler
            });
    }

    /// <summary>
    /// Stops the Inventory test host and PostgreSQL container.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token used to cancel shutdown.
    /// </param>
    public async Task StopAsync(
        CancellationToken cancellationToken) {
        Dispose();

        await _postgresContainer.StopAsync(
            cancellationToken);

        await _postgresContainer.DisposeAsync();
    }

    /// <summary>
    /// Configures the Inventory ASP.NET Core test host.
    /// </summary>
    /// <param name="builder">
    /// The web host builder being configured.
    /// </param>
    protected override void ConfigureWebHost(
        IWebHostBuilder builder) {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<
                DbContextOptions<InventoryDbContext>>();

            services.AddDbContext<InventoryDbContext>(
                options =>
                    options.UseNpgsql(
                        _postgresContainer.GetConnectionString()));
        });

        builder.UseEnvironment("Development");
    }
}