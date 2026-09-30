using Testcontainers.PostgreSql;
using Xunit;

namespace Conflux.Inventory.IntegrationTests.Infrastructure;

/// <summary>
/// Provides shared PostgreSQL infrastructure for Inventory integration tests.
/// </summary>
public sealed class InventoryTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer =
        new PostgreSqlBuilder("postgres:17")
            .WithDatabase("conflux_inventory_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    /// <summary>
    /// Gets the PostgreSQL test container.
    /// </summary>
    public PostgreSqlContainer PostgresContainer =>
        _postgresContainer;

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