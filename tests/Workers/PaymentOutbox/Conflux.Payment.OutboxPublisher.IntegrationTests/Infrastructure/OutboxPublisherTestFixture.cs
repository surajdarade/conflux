using Conflux.Payment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Xunit;

namespace Conflux.Payment.OutboxPublisher.IntegrationTests.Infrastructure;

/// <summary>
/// Provides PostgreSQL and Kafka containers for Outbox Publisher
/// integration tests.
/// </summary>
public sealed class OutboxPublisherTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer =
        new PostgreSqlBuilder("postgres:17")
            .WithDatabase("conflux_payment_tests")
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
    /// Gets the Kafka bootstrap server address.
    /// </summary>
    public string KafkaBootstrapAddress =>
        _kafkaContainer.GetBootstrapAddress();

    /// <summary>
    /// Initializes the PostgreSQL and Kafka containers.
    /// </summary>
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

    /// <summary>
    /// Releases the PostgreSQL and Kafka containers.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _kafkaContainer.DisposeAsync();
        await _postgresContainer.DisposeAsync();
    }

    /// <summary>
    /// Creates a Payment database context connected to the test PostgreSQL container.
    /// </summary>
    /// <returns>
    /// A configured Payment database context.
    /// </returns>
    public PaymentDbContext CreateDbContext()
    {
        var options =
            new DbContextOptionsBuilder<PaymentDbContext>()
                .UseNpgsql(PostgresConnectionString)
                .Options;

        return new PaymentDbContext(options);
    }
}