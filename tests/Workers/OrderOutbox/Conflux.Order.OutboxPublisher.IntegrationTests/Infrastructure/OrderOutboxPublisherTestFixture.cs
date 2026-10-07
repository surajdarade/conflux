using Conflux.Order.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Xunit;

namespace Conflux.Order.OutboxPublisher.IntegrationTests.Infrastructure;

/// <summary>
/// Provides PostgreSQL and Kafka containers shared by Order Outbox
/// Publisher integration tests.
/// </summary>
public sealed class OrderOutboxPublisherTestFixture :
    IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer =
        new PostgreSqlBuilder("postgres:17")
            .WithDatabase("conflux_order_tests")
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
    /// Creates a new Order database context connected to the test database.
    /// </summary>
    /// <returns>
    /// A configured Order database context.
    /// </returns>
    public OrderDbContext CreateDbContext()
    {
        var options =
            new DbContextOptionsBuilder<OrderDbContext>()
                .UseNpgsql(PostgresConnectionString)
                .Options;

        return new OrderDbContext(options);
    }
}