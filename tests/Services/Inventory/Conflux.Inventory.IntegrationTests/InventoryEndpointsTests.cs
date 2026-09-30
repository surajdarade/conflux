using System.Net;
using System.Net.Http.Json;
using Conflux.Inventory.Features.Inventory.CreateInventory;
using Conflux.Inventory.Features.Inventory.GetInventory;
using Conflux.Inventory.Features.Inventory.ReserveInventory;
using Conflux.Inventory.IntegrationTests.Infrastructure;
using Xunit;

namespace Conflux.Inventory.IntegrationTests;

/// <summary>
/// Provides integration tests for the Inventory endpoints.
/// </summary>
[Collection(InventoryTestCollection.Name)]
public sealed class InventoryEndpointsTests
{
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="InventoryEndpointsTests"/> class.
    /// </summary>
    /// <param name="fixture">
    /// The shared Inventory integration-test infrastructure.
    /// </param>
    public InventoryEndpointsTests(
        InventoryTestFixture fixture)
    {
        _factory = new InventoryApiFactory(
            fixture.PostgresContainer);

        _client = _factory.CreateClient();

        _factory
            .ApplyDatabaseMigrationsAsync(
                TestContext.Current.CancellationToken)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Verifies that valid inventory can be created.
    /// </summary>
    [Fact]
    public async Task CreateInventory_WithValidRequest_ReturnsCreated()
    {
        var request = CreateRequest(10);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/inventory/items",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<CreateInventoryResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result!.InventoryId);
        Assert.Equal(
            request.Sku.ToUpperInvariant(),
            result.Sku);
        Assert.Equal(
            10,
            result.AvailableQuantity);
        Assert.Equal(
            0,
            result.ReservedQuantity);
    }

    /// <summary>
    /// Verifies that duplicate SKUs are rejected.
    /// </summary>
    [Fact]
    public async Task CreateInventory_WithDuplicateSku_ReturnsConflict()
    {
        var request = CreateRequest(10);

        var firstResponse = await _client.PostAsJsonAsync(
            "/api/v1/inventory/items",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);

        var secondResponse = await _client.PostAsJsonAsync(
            "/api/v1/inventory/items",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode);
    }

    /// <summary>
    /// Verifies that negative inventory quantities are rejected.
    /// </summary>
    [Fact]
    public async Task CreateInventory_WithNegativeQuantity_ReturnsBadRequest()
    {
        var request = CreateRequest(-1);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/inventory/items",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that an existing inventory item can be retrieved.
    /// </summary>
    [Fact]
    public async Task GetInventory_WithExistingInventory_ReturnsOk()
    {
        var created = await CreateInventoryAsync(25);

        var response = await _client.GetAsync(
            $"/api/v1/inventory/items/{created.InventoryId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<GetInventoryResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(
            created.InventoryId,
            result!.InventoryId);
        Assert.Equal(
            created.Sku,
            result.Sku);
        Assert.Equal(
            25,
            result.AvailableQuantity);
        Assert.Equal(
            0,
            result.ReservedQuantity);
    }

    /// <summary>
    /// Verifies that retrieving an unknown inventory item returns not found.
    /// </summary>
    [Fact]
    public async Task GetInventory_WithUnknownInventory_ReturnsNotFound()
    {
        var response = await _client.GetAsync(
            $"/api/v1/inventory/items/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that a valid inventory reservation decreases available inventory.
    /// </summary>
    [Fact]
    public async Task ReserveInventory_WithSufficientInventory_ReturnsOk()
    {
        var created = await CreateInventoryAsync(10);

        var request = new ReserveInventoryRequest
        {
            Quantity = 3
        };

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/items/{created.InventoryId}/reservations",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<ReserveInventoryResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(
            created.InventoryId,
            result!.InventoryId);
        Assert.Equal(
            created.Sku,
            result.Sku);
        Assert.Equal(
            3,
            result.ReservedQuantity);
        Assert.Equal(
            7,
            result.AvailableQuantity);
        Assert.Equal(
            3,
            result.TotalReservedQuantity);
    }

    /// <summary>
    /// Verifies that a reservation exceeding available inventory is rejected.
    /// </summary>
    [Fact]
    public async Task ReserveInventory_WithInsufficientInventory_ReturnsConflict()
    {
        var created = await CreateInventoryAsync(5);

        var request = new ReserveInventoryRequest
        {
            Quantity = 6
        };

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/items/{created.InventoryId}/reservations",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that a non-positive reservation quantity is rejected.
    /// </summary>
    /// <param name="quantity">
    /// The invalid reservation quantity.
    /// </param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReserveInventory_WithInvalidQuantity_ReturnsBadRequest(
        int quantity)
    {
        var created = await CreateInventoryAsync(10);

        var request = new ReserveInventoryRequest
        {
            Quantity = quantity
        };

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/items/{created.InventoryId}/reservations",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    private async Task<CreateInventoryResponse> CreateInventoryAsync(
        int quantity)
    {
        var request = CreateRequest(quantity);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/inventory/items",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<CreateInventoryResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        return result!;
    }

    private static CreateInventoryRequest CreateRequest(
        int availableQuantity)
    {
        return new CreateInventoryRequest
        {
            Sku = $"SKU-{Guid.NewGuid():N}",
            AvailableQuantity = availableQuantity
        };
    }
}