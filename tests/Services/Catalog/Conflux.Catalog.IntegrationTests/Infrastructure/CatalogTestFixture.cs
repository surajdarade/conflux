using Testcontainers.PostgreSql;
using Xunit;

namespace Conflux.Catalog.IntegrationTests.Infrastructure;

/// <summary>
/// Provides shared PostgreSQL infrastructure for Catalog integration tests.
/// </summary>
public sealed class CatalogTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer =
        new PostgreSqlBuilder("postgres:17")
            .WithDatabase("conflux_catalog_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    /// <summary>
    /// Gets the PostgreSQL test container.
    /// </summary>
    public PostgreSqlContainer PostgresContainer => _postgresContainer;

    /// <summary>
    /// Starts the PostgreSQL test container.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        await _postgresContainer.StartAsync();
    }

    /// <summary>
    /// Stops and disposes the PostgreSQL test container.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }
}