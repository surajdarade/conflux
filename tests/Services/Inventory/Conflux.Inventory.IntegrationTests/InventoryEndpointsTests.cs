using System.Net;
using System.Net.Http.Json;
using Conflux.Inventory.Features.Inventory.CreateInventory;
using Conflux.Inventory.Features.Inventory.GetInventory;
using Conflux.Inventory.Features.Inventory.ReleaseInventory;
using Conflux.Inventory.Features.Inventory.ReserveInventory;
using Conflux.Inventory.IntegrationTests.Infrastructure;
using Xunit;

namespace Conflux.Inventory.IntegrationTests;

/// <summary>
/// Provides integration tests for the Inventory endpoints.
/// </summary>
[Collection(InventoryTestCollection.Name)]
public sealed class InventoryEndpointsTests {
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="InventoryEndpointsTests"/> class.
    /// </summary>
    /// <param name="fixture">
    /// The shared Inventory integration-test infrastructure.
    /// </param>
    public InventoryEndpointsTests(
        InventoryTestFixture fixture) {
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
    public async Task CreateInventory_WithValidRequest_ReturnsCreated() {
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
    public async Task CreateInventory_WithDuplicateSku_ReturnsConflict() {
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
    public async Task CreateInventory_WithNegativeQuantity_ReturnsBadRequest() {
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
    public async Task GetInventory_WithExistingInventory_ReturnsOk() {
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
    public async Task GetInventory_WithUnknownInventory_ReturnsNotFound() {
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
    public async Task ReserveInventory_WithSufficientInventory_ReturnsOk() {
        var created = await CreateInventoryAsync(10);
        var reservationId = Guid.NewGuid();

        var response = await ReserveAsync(
            created.InventoryId,
            reservationId,
            3);

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
    public async Task ReserveInventory_WithInsufficientInventory_ReturnsConflict() {
        var created = await CreateInventoryAsync(5);

        var response = await ReserveAsync(
            created.InventoryId,
            Guid.NewGuid(),
            6);

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
        int quantity) {
        var created = await CreateInventoryAsync(10);

        var response = await ReserveAsync(
            created.InventoryId,
            Guid.NewGuid(),
            quantity);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that the same idempotent reservation request does not
    /// consume inventory more than once.
    /// </summary>
    [Fact]
    public async Task ReserveInventory_WithSameIdempotencyKey_ReturnsOriginalReservation() {
        var created = await CreateInventoryAsync(10);
        var reservationId = Guid.NewGuid();

        var firstResponse = await ReserveAsync(
            created.InventoryId,
            reservationId,
            3);

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);

        var firstResult = await firstResponse.Content
            .ReadFromJsonAsync<ReserveInventoryResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(firstResult);

        var secondResponse = await ReserveAsync(
            created.InventoryId,
            reservationId,
            3);

        Assert.Equal(
            HttpStatusCode.OK,
            secondResponse.StatusCode);

        var secondResult = await secondResponse.Content
            .ReadFromJsonAsync<ReserveInventoryResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(secondResult);

        Assert.Equal(
            firstResult!.InventoryId,
            secondResult!.InventoryId);

        Assert.Equal(
            firstResult.ReservedQuantity,
            secondResult.ReservedQuantity);

        var inventory = await GetInventoryAsync(
            created.InventoryId);

        Assert.Equal(
            7,
            inventory.AvailableQuantity);

        Assert.Equal(
            3,
            inventory.ReservedQuantity);
    }

    /// <summary>
    /// Verifies that reusing an idempotency key with a different quantity
    /// is rejected.
    /// </summary>
    [Fact]
    public async Task ReserveInventory_WithSameIdempotencyKeyAndDifferentQuantity_ReturnsConflict() {
        var created = await CreateInventoryAsync(10);
        var reservationId = Guid.NewGuid();

        var firstResponse = await ReserveAsync(
            created.InventoryId,
            reservationId,
            3);

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);

        var secondResponse = await ReserveAsync(
            created.InventoryId,
            reservationId,
            4);

        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode);

        var inventory = await GetInventoryAsync(
            created.InventoryId);

        Assert.Equal(
            7,
            inventory.AvailableQuantity);

        Assert.Equal(
            3,
            inventory.ReservedQuantity);
    }

    /// <summary>
    /// Verifies that concurrent reservations cannot allocate more inventory
    /// than is available.
    /// </summary>
    [Fact]
    public async Task ReserveInventory_ConcurrentReservations_NeverOverAllocatesInventory() {
        const int initialQuantity = 100;
        const int reservationCount = 200;

        var created = await CreateInventoryAsync(initialQuantity);

        var tasks = Enumerable
            .Range(0, reservationCount)
            .Select(
                _ => ReserveOneAsync(
                    created.InventoryId))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        var successfulReservations = responses
            .Count(response =>
                response.StatusCode == HttpStatusCode.OK);

        var rejectedReservations = responses
            .Count(response =>
                response.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(
            initialQuantity,
            successfulReservations);

        Assert.Equal(
            reservationCount - initialQuantity,
            rejectedReservations);

        var finalInventory = await GetInventoryAsync(
            created.InventoryId);

        Assert.Equal(
            0,
            finalInventory.AvailableQuantity);

        Assert.Equal(
            initialQuantity,
            finalInventory.ReservedQuantity);
    }

    /// <summary>
    /// Verifies that an existing reservation can be released.
    /// </summary>
    [Fact]
    public async Task ReleaseInventory_WithActiveReservation_ReturnsOk() {
        var created = await CreateInventoryAsync(10);
        var reservationId = Guid.NewGuid();

        var reserveResponse = await ReserveAsync(
            created.InventoryId,
            reservationId,
            3);

        Assert.Equal(
            HttpStatusCode.OK,
            reserveResponse.StatusCode);

        var releaseResponse = await ReleaseAsync(
            created.InventoryId,
            reservationId);

        Assert.Equal(
            HttpStatusCode.OK,
            releaseResponse.StatusCode);

        var result = await releaseResponse.Content
            .ReadFromJsonAsync<ReleaseInventoryResponse>(
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
            result.ReleasedQuantity);
        Assert.Equal(
            10,
            result.AvailableQuantity);
        Assert.Equal(
            0,
            result.TotalReservedQuantity);
        Assert.False(result.AlreadyReleased);
    }

    /// <summary>
    /// Verifies that releasing the same reservation twice does not
    /// return inventory twice.
    /// </summary>
    [Fact]
    public async Task ReleaseInventory_WithSameReservation_ReturnsIdempotentResult() {
        var created = await CreateInventoryAsync(10);
        var reservationId = Guid.NewGuid();

        await ReserveAsync(
            created.InventoryId,
            reservationId,
            3);

        var firstResponse = await ReleaseAsync(
            created.InventoryId,
            reservationId);

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);

        var secondResponse = await ReleaseAsync(
            created.InventoryId,
            reservationId);

        Assert.Equal(
            HttpStatusCode.OK,
            secondResponse.StatusCode);

        var result = await secondResponse.Content
            .ReadFromJsonAsync<ReleaseInventoryResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result!.AlreadyReleased);
        Assert.Equal(
            10,
            result.AvailableQuantity);
        Assert.Equal(
            0,
            result.TotalReservedQuantity);

        var inventory = await GetInventoryAsync(
            created.InventoryId);

        Assert.Equal(
            10,
            inventory.AvailableQuantity);

        Assert.Equal(
            0,
            inventory.ReservedQuantity);
    }

    /// <summary>
    /// Verifies that concurrent release requests cannot release the same
    /// reservation more than once.
    /// </summary>
    [Fact]
    public async Task ReleaseInventory_ConcurrentRequests_ReleasesOnlyOnce() {
        var created = await CreateInventoryAsync(10);
        var reservationId = Guid.NewGuid();

        var reserveResponse = await ReserveAsync(
            created.InventoryId,
            reservationId,
            3);

        Assert.Equal(
            HttpStatusCode.OK,
            reserveResponse.StatusCode);

        var tasks = Enumerable
            .Range(0, 20)
            .Select(
                _ => ReleaseAsync(
                    created.InventoryId,
                    reservationId))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(
            responses,
            response =>
                Assert.Equal(
                    HttpStatusCode.OK,
                    response.StatusCode));

        var inventory = await GetInventoryAsync(
            created.InventoryId);

        Assert.Equal(
            10,
            inventory.AvailableQuantity);

        Assert.Equal(
            0,
            inventory.ReservedQuantity);
    }

    /// <summary>
    /// Verifies that releasing a reservation against another inventory item
    /// is rejected.
    /// </summary>
    [Fact]
    public async Task ReleaseInventory_WithWrongInventory_ReturnsConflict() {
        var firstInventory = await CreateInventoryAsync(10);
        var secondInventory = await CreateInventoryAsync(10);
        var reservationId = Guid.NewGuid();

        var reserveResponse = await ReserveAsync(
            firstInventory.InventoryId,
            reservationId,
            3);

        Assert.Equal(
            HttpStatusCode.OK,
            reserveResponse.StatusCode);

        var releaseResponse = await ReleaseAsync(
            secondInventory.InventoryId,
            reservationId);

        Assert.Equal(
            HttpStatusCode.Conflict,
            releaseResponse.StatusCode);

        var firstState = await GetInventoryAsync(
            firstInventory.InventoryId);

        var secondState = await GetInventoryAsync(
            secondInventory.InventoryId);

        Assert.Equal(
            7,
            firstState.AvailableQuantity);

        Assert.Equal(
            3,
            firstState.ReservedQuantity);

        Assert.Equal(
            10,
            secondState.AvailableQuantity);

        Assert.Equal(
            0,
            secondState.ReservedQuantity);
    }

    /// <summary>
    /// Verifies that an unknown reservation returns not found.
    /// </summary>
    [Fact]
    public async Task ReleaseInventory_WithUnknownReservation_ReturnsNotFound() {
        var created = await CreateInventoryAsync(10);

        var response = await ReleaseAsync(
            created.InventoryId,
            Guid.NewGuid());

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    private async Task<HttpResponseMessage> ReserveOneAsync(
        Guid inventoryId) {
        return await ReserveAsync(
            inventoryId,
            Guid.NewGuid(),
            1);
    }

    private async Task<HttpResponseMessage> ReserveAsync(
        Guid inventoryId,
        Guid reservationId,
        int quantity) {
        var request = new ReserveInventoryRequest
        {
            Quantity = quantity
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inventory/items/{inventoryId}/reservations")
        {
            Content = JsonContent.Create(request)
        };

        httpRequest.Headers.Add(
            "Idempotency-Key",
            reservationId.ToString());

        return await _client.SendAsync(
            httpRequest,
            TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> ReleaseAsync(
        Guid inventoryId,
        Guid reservationId) {
        return await _client.PostAsync(
            $"/api/v1/inventory/items/{inventoryId}/reservations/{reservationId}/release",
            null,
            TestContext.Current.CancellationToken);
    }

    private async Task<GetInventoryResponse> GetInventoryAsync(
        Guid inventoryId) {
        var response = await _client.GetAsync(
            $"/api/v1/inventory/items/{inventoryId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<GetInventoryResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        return result!;
    }

    private async Task<CreateInventoryResponse> CreateInventoryAsync(
        int quantity) {
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
        int availableQuantity) {
        return new CreateInventoryRequest
        {
            Sku = $"SKU-{Guid.NewGuid():N}",
            AvailableQuantity = availableQuantity
        };
    }
}