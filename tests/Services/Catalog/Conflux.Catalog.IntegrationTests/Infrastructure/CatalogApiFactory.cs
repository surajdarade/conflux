using Conflux.Catalog.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Conflux.Catalog.IntegrationTests.Infrastructure;

/// <summary>
/// Provides a PostgreSQL-backed ASP.NET Core test host for Catalog integration tests.
/// </summary>
public sealed class CatalogApiFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgresContainer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogApiFactory"/> class.
    /// </summary>
    /// <param name="postgresContainer">
    /// The PostgreSQL container used by the test host.
    /// </param>
    public CatalogApiFactory(
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
            "ConnectionStrings:CatalogDatabase",
            _postgresContainer.GetConnectionString());

        builder.UseEnvironment("Development");
    }

    /// <summary>
    /// Applies the Catalog database migrations to the test database.
    /// </summary>
    public async Task ApplyDatabaseMigrationsAsync(
        CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<CatalogDbContext>();

        await dbContext.Database.MigrateAsync(
            cancellationToken);
    }
}