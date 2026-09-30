using System.Net;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;
using Xunit;

namespace Conflux.Order.IntegrationTests.Infrastructure;

/// <summary>
/// Provides shared PostgreSQL and Inventory service infrastructure
/// for Order integration tests.
/// </summary>
public sealed class OrderTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer =
        new PostgreSqlBuilder("postgres:17")
            .WithDatabase("conflux_order_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private readonly InventoryGrpcTestFactory _inventoryFactory =
        new();

    /// <summary>
    /// Gets the PostgreSQL test container used by the Order service.
    /// </summary>
    public PostgreSqlContainer PostgresContainer =>
        _postgresContainer;

    /// <summary>
    /// Gets the Inventory service test factory.
    /// </summary>
    public InventoryGrpcTestFactory InventoryFactory =>
        _inventoryFactory;

    /// <summary>
    /// Starts the PostgreSQL and Inventory test infrastructure.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        await _postgresContainer.StartAsync();

        await _inventoryFactory.StartAsync(
            TestContext.Current.CancellationToken);

        await SeedInventoryAsync(
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Stops and disposes the PostgreSQL and Inventory test infrastructure.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _inventoryFactory.StopAsync(
            TestContext.Current.CancellationToken);

        await _postgresContainer.DisposeAsync();
    }

    private async Task SeedInventoryAsync(
        CancellationToken cancellationToken)
    {
        using var client = _inventoryFactory.CreateClient();

        await CreateInventoryItemAsync(
            client,
            "CONFLUX-001",
            1_000,
            cancellationToken);

        await CreateInventoryItemAsync(
            client,
            "CONFLUX-CONCURRENT",
            1_000,
            cancellationToken);

        await CreateInventoryItemAsync(
            client,
            "CONFLUX-GET-001",
            1_000,
            cancellationToken);
    }

    private static async Task CreateInventoryItemAsync(
        HttpClient client,
        string sku,
        int availableQuantity,
        CancellationToken cancellationToken)
    {
        var response =
            await client.PostAsJsonAsync(
                "/api/v1/inventory/items",
                new
                {
                    Sku = sku,
                    AvailableQuantity = availableQuantity
                },
                cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }
}