using Conflux.Inventory.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Conflux.Inventory.IntegrationTests.Infrastructure;

/// <summary>
/// Provides a PostgreSQL-backed ASP.NET Core test host for Inventory integration tests.
/// </summary>
public sealed class InventoryApiFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgresContainer;

    /// <summary>
    /// Initializes a new instance of the <see cref="InventoryApiFactory"/> class.
    /// </summary>
    /// <param name="postgresContainer">
    /// The PostgreSQL container used by the test host.
    /// </param>
    public InventoryApiFactory(
        PostgreSqlContainer postgresContainer)
    {
        _postgresContainer = postgresContainer;
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
            "ConnectionStrings:InventoryDatabase",
            _postgresContainer.GetConnectionString());

        builder.UseEnvironment("Development");
    }

    /// <summary>
    /// Applies the Inventory database migrations to the test database.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token used to cancel the migration operation.
    /// </param>
    public async Task ApplyDatabaseMigrationsAsync(
        CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<InventoryDbContext>();

        await dbContext.Database.MigrateAsync(
            cancellationToken);
    }
}