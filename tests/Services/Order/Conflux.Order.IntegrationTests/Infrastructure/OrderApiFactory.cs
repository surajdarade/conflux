extern alias Order;

using Conflux.Order.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using OrderProgram = Order::Program;

namespace Conflux.Order.IntegrationTests.Infrastructure;

/// <summary>
/// Provides a PostgreSQL-backed ASP.NET Core test host for Order integration tests.
/// </summary>
public sealed class OrderApiFactory :
    WebApplicationFactory<OrderProgram> {
    private readonly PostgreSqlContainer _postgresContainer;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderApiFactory"/> class.
    /// </summary>
    /// <param name="postgresContainer">
    /// The PostgreSQL container used by the test host.
    /// </param>
    public OrderApiFactory(
        PostgreSqlContainer postgresContainer) {
        _postgresContainer = postgresContainer;
    }

    /// <summary>
    /// Configures the ASP.NET Core host used by the integration tests.
    /// </summary>
    /// <param name="builder">
    /// The web host builder being configured.
    /// </param>
    protected override void ConfigureWebHost(
        IWebHostBuilder builder) {
        builder.UseSetting(
            "ConnectionStrings:OrderDatabase",
            _postgresContainer.GetConnectionString());

        builder.UseEnvironment("Development");
    }

    /// <summary>
    /// Applies the Order database migrations to the test database.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token used to cancel the migration operation.
    /// </param>
    public async Task ApplyDatabaseMigrationsAsync(
        CancellationToken cancellationToken) {
        using var scope = Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<OrderDbContext>();

        await dbContext.Database.MigrateAsync(
            cancellationToken);
    }
}