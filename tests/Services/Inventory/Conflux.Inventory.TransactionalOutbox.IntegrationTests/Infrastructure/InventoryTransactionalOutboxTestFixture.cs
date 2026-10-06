using Conflux.Inventory.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Conflux.Inventory.TransactionalOutbox.IntegrationTests.Infrastructure;

/// <summary>
/// Provides a real PostgreSQL database for Inventory transactional Outbox
/// integration tests.
/// </summary>
public sealed class InventoryTransactionalOutboxTestFixture :
    IAsyncLifetime {
    private readonly PostgreSqlContainer _postgresContainer =
        new PostgreSqlBuilder("postgres:17")
            .WithDatabase("conflux_inventory_outbox_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    /// <summary>
    /// Gets the PostgreSQL connection string.
    /// </summary>
    public string ConnectionString =>
        _postgresContainer.GetConnectionString();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() {
        await _postgresContainer.StartAsync(
            TestContext.Current.CancellationToken);

        await using var dbContext =
            CreateDbContext();

        await dbContext.Database.MigrateAsync(
            TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        await _postgresContainer.DisposeAsync();
    }

    /// <summary>
    /// Creates an Inventory database context connected to the test database.
    /// </summary>
    /// <returns>
    /// A configured Inventory database context.
    /// </returns>
    public InventoryDbContext CreateDbContext() {
        var options =
            new DbContextOptionsBuilder<InventoryDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

        return new InventoryDbContext(options);
    }
}