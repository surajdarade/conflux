using Conflux.Inventory.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Xunit;

namespace Conflux.Inventory.OutboxPublisher.IntegrationTests.Infrastructure;

/// <summary>
/// Provides PostgreSQL and Kafka containers shared by Inventory Outbox
/// Publisher integration tests.
/// </summary>
public sealed class InventoryOutboxPublisherTestFixture :
    IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer =
        new PostgreSqlBuilder("postgres:17")
            .WithDatabase("conflux_inventory_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private readonly KafkaContainer _kafkaContainer =
        new KafkaBuilder("confluentinc/cp-kafka:7.5.12")
            .Build();

    /// <summary>
    /// Gets the PostgreSQL connection string.
    /// </summary>
    public string PostgresConnectionString =>
        _postgresContainer.GetConnectionString();

    /// <summary>
    /// Gets the Kafka bootstrap address.
    /// </summary>
    public string KafkaBootstrapAddress =>
        _kafkaContainer.GetBootstrapAddress();

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await _postgresContainer.StartAsync(
            TestContext.Current.CancellationToken);

        await _kafkaContainer.StartAsync(
            TestContext.Current.CancellationToken);

        await using var dbContext =
            CreateDbContext();

        await dbContext.Database.MigrateAsync(
            TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _kafkaContainer.DisposeAsync();
        await _postgresContainer.DisposeAsync();
    }

    /// <summary>
    /// Creates a new Inventory database context connected to the test database.
    /// </summary>
    /// <returns>
    /// A configured Inventory database context.
    /// </returns>
    public InventoryDbContext CreateDbContext()
    {
        var options =
            new DbContextOptionsBuilder<InventoryDbContext>()
                .UseNpgsql(PostgresConnectionString)
                .Options;

        return new InventoryDbContext(options);
    }
}