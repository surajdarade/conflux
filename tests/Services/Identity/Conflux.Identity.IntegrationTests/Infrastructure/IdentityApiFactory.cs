using Conflux.Identity.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Conflux.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Provides a PostgreSQL-backed ASP.NET Core test host for Identity integration tests.
/// </summary>
public sealed class IdentityApiFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgresContainer;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityApiFactory"/> class.
    /// </summary>
    /// <param name="postgresContainer">
    /// The PostgreSQL container used by the test host.
    /// </param>
    public IdentityApiFactory(PostgreSqlContainer postgresContainer)
    {
        _postgresContainer = postgresContainer;
    }

    /// <summary>
    /// Configures the ASP.NET Core host used by the integration tests.
    /// </summary>
    /// <param name="builder">
    /// The web host builder being configured.
    /// </param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "ConnectionStrings:IdentityDatabase",
            _postgresContainer.GetConnectionString());

        builder.UseEnvironment("Development");
    }

    /// <summary>
    /// Applies the Identity database migrations to the test database.
    /// </summary>
    public async Task ApplyDatabaseMigrationsAsync(
        CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<IdentityDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}