using Testcontainers.PostgreSql;
using Xunit;

namespace Conflux.Payment.IntegrationTests.Infrastructure;

/// <summary>
/// Provides shared PostgreSQL infrastructure
/// for Payment integration tests.
/// </summary>
public sealed class PaymentTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer =
        new PostgreSqlBuilder("postgres:17")
            .WithDatabase("conflux_payment_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    /// <summary>
    /// Gets the PostgreSQL container used by the tests.
    /// </summary>
    public PostgreSqlContainer PostgresContainer =>
        _postgresContainer;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await _postgresContainer.StartAsync(
            TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }
}