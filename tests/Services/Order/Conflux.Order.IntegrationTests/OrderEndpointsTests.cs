using System.Net;
using System.Net.Http.Json;
using Conflux.Order.Domain;
using Conflux.Order.Features.Orders.CreateOrder;
using Conflux.Order.IntegrationTests.Infrastructure;
using Xunit;

namespace Conflux.Order.IntegrationTests;

/// <summary>
/// Provides integration tests for the Order HTTP endpoints.
/// </summary>
[Collection(OrderTestCollection.Name)]
public sealed class OrderEndpointsTests :
    IAsyncLifetime {
    private readonly OrderApiFactory _factory;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderEndpointsTests"/> class.
    /// </summary>
    /// <param name="fixture">
    /// The shared Order test infrastructure.
    /// </param>
    public OrderEndpointsTests(
        OrderTestFixture fixture) {
        _factory = new OrderApiFactory(
            fixture.PostgresContainer,
            fixture.InventoryFactory);

        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Applies the database migrations before the test suite runs.
    /// </summary>
    public async ValueTask InitializeAsync() {
        await _factory.ApplyDatabaseMigrationsAsync(
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Disposes the HTTP client and test host after the test suite completes.
    /// </summary>
    public async ValueTask DisposeAsync() {
        _client.Dispose();

        await _factory.DisposeAsync();
    }

    /// <summary>
    /// Verifies that a valid order creation request returns HTTP 201 Created
    /// with the expected order and item data.
    /// </summary>
    [Fact]
    public async Task CreateOrder_WithValidRequest_ReturnsCreated() {
        var customerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString();

        var request = CreateRequest(
            customerId,
            "CONFLUX-001",
            2,
            125.50m,
            "INR");

        using var httpRequest = CreateHttpRequest(
            request,
            idempotencyKey);

        var response = await _client.SendAsync(
            httpRequest,
            TestContext.Current.CancellationToken);

        var responseBody =
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken);

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 Created but received {(int)response.StatusCode} {response.StatusCode}. Response body: {responseBody}");

        var result =
            await response.Content.ReadFromJsonAsync<CreateOrderResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.OrderId);
        Assert.Equal(customerId, result.CustomerId);
        Assert.Equal(
            OrderStatus.InventoryReserved,
            result.Status);
        Assert.Equal(251.00m, result.TotalAmount);
        Assert.Equal("INR", result.Currency);

        Assert.Single(result.Items);

        Assert.Equal(
            "CONFLUX-001",
            result.Items[0].Sku);

        Assert.Equal(
            2,
            result.Items[0].Quantity);

        Assert.Equal(
            125.50m,
            result.Items[0].UnitPrice);

        Assert.Equal(
            251.00m,
            result.Items[0].LineTotal);
    }

    /// <summary>
    /// Verifies that an order creation request without an idempotency key
    /// returns HTTP 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task CreateOrder_WithoutIdempotencyKey_ReturnsBadRequest() {
        var request = CreateRequest(
            Guid.NewGuid(),
            "CONFLUX-001",
            1,
            100m,
            "INR");

        var response = await _client.PostAsJsonAsync(
            "/api/v1/orders",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that an order creation request without any items
    /// returns HTTP 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task CreateOrder_WithEmptyItems_ReturnsBadRequest() {
        var request = new CreateOrderRequest
        {
            CustomerId = Guid.NewGuid(),
            Items = []
        };

        using var httpRequest = CreateHttpRequest(
            request,
            Guid.NewGuid().ToString());

        var response = await _client.SendAsync(
            httpRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that an order item with a non-positive quantity
    /// returns HTTP 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task CreateOrder_WithInvalidQuantity_ReturnsBadRequest() {
        var request = CreateRequest(
            Guid.NewGuid(),
            "CONFLUX-001",
            0,
            100m,
            "INR");

        using var httpRequest = CreateHttpRequest(
            request,
            Guid.NewGuid().ToString());

        var response = await _client.SendAsync(
            httpRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that an order item with a negative price
    /// returns HTTP 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task CreateOrder_WithNegativePrice_ReturnsBadRequest() {
        var request = CreateRequest(
            Guid.NewGuid(),
            "CONFLUX-001",
            1,
            -1m,
            "INR");

        using var httpRequest = CreateHttpRequest(
            request,
            Guid.NewGuid().ToString());

        var response = await _client.SendAsync(
            httpRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that repeating an order creation request with the same
    /// idempotency key returns the previously created order instead of
    /// creating a duplicate order.
    /// </summary>
    [Fact]
    public async Task CreateOrder_WithSameIdempotencyKey_ReturnsSameOrder() {
        var customerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString();

        var request = CreateRequest(
            customerId,
            "CONFLUX-001",
            2,
            150m,
            "INR");

        using var firstRequest = CreateHttpRequest(
            request,
            idempotencyKey);

        using var firstResponse = await _client.SendAsync(
            firstRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);

        var firstResult =
            await firstResponse.Content.ReadFromJsonAsync<CreateOrderResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(firstResult);

        using var secondRequest = CreateHttpRequest(
            request,
            idempotencyKey);

        using var secondResponse = await _client.SendAsync(
            secondRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            secondResponse.StatusCode);

        var secondResult =
            await secondResponse.Content.ReadFromJsonAsync<CreateOrderResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(secondResult);

        Assert.Equal(
            firstResult.OrderId,
            secondResult.OrderId);

        Assert.Equal(
            firstResult.TotalAmount,
            secondResult.TotalAmount);
    }

    /// <summary>
    /// Verifies that reusing an idempotency key for a different customer
    /// returns HTTP 409 Conflict.
    /// </summary>
    [Fact]
    public async Task CreateOrder_WithSameIdempotencyKeyAndDifferentCustomer_ReturnsConflict() {
        var idempotencyKey = Guid.NewGuid().ToString();

        var firstRequest = CreateRequest(
            Guid.NewGuid(),
            "CONFLUX-001",
            1,
            100m,
            "INR");

        using var firstHttpRequest = CreateHttpRequest(
            firstRequest,
            idempotencyKey);

        var firstResponse = await _client.SendAsync(
            firstHttpRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);

        var secondRequest = CreateRequest(
            Guid.NewGuid(),
            "CONFLUX-001",
            1,
            100m,
            "INR");

        using var secondHttpRequest = CreateHttpRequest(
            secondRequest,
            idempotencyKey);

        var secondResponse = await _client.SendAsync(
            secondHttpRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode);
    }

    /// <summary>
    /// Verifies that concurrent requests using the same idempotency key
    /// result in exactly one persisted order and all other requests
    /// resolve to that same order.
    /// </summary>
    [Fact]
    public async Task CreateOrder_ConcurrentRequestsWithSameIdempotencyKey_CreateExactlyOneOrder() {
        var customerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString();

        var request = CreateRequest(
            customerId,
            "CONFLUX-CONCURRENT",
            1,
            200m,
            "INR");

        const int requestCount = 20;

        var tasks = Enumerable
            .Range(0, requestCount)
            .Select(
                _ => SendCreateOrderRequestAsync(
                    request,
                    idempotencyKey))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        foreach (var response in responses) {
            if (response.StatusCode != HttpStatusCode.Created &&
                response.StatusCode != HttpStatusCode.OK) {
                var body =
                    await response.Content.ReadAsStringAsync(
                        TestContext.Current.CancellationToken);

                Assert.Fail(
                    $"Unexpected response: {(int)response.StatusCode} " +
                    $"{response.StatusCode}. Body: {body}");
            }
        }

        var createdCount = responses.Count(
            response =>
                response.StatusCode ==
                HttpStatusCode.Created);

        var successfulRetryCount = responses.Count(
            response =>
                response.StatusCode ==
                HttpStatusCode.OK);

        Assert.Equal(
            1,
            createdCount);

        Assert.Equal(
            requestCount - 1,
            successfulRetryCount);

        var orderIds = new List<Guid>();

        foreach (var response in responses) {
            var result =
                await response.Content.ReadFromJsonAsync<CreateOrderResponse>(
                    TestContext.Current.CancellationToken);

            Assert.NotNull(result);

            orderIds.Add(result.OrderId);
        }

        Assert.Single(
            orderIds.Distinct());
    }

    /// <summary>
    /// Verifies that an existing order can be retrieved by its identifier.
    /// </summary>
    [Fact]
    public async Task GetOrder_WithExistingOrder_ReturnsOrder() {
        var customerId = Guid.NewGuid();

        var request = CreateRequest(
            customerId,
            "CONFLUX-GET-001",
            3,
            75m,
            "INR");

        using var createRequest = CreateHttpRequest(
            request,
            Guid.NewGuid().ToString());

        using var createResponse = await _client.SendAsync(
            createRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            createResponse.StatusCode);

        var createdOrder =
            await createResponse.Content.ReadFromJsonAsync<CreateOrderResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(createdOrder);

        var response = await _client.GetAsync(
            $"/api/v1/orders/{createdOrder.OrderId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result =
            await response.Content.ReadFromJsonAsync<GetOrderResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        Assert.Equal(
            createdOrder.OrderId,
            result.OrderId);

        Assert.Equal(
            customerId,
            result.CustomerId);

        Assert.Equal(
            OrderStatus.InventoryReserved,
            result.Status);

        Assert.Equal(
            225m,
            result.TotalAmount);

        Assert.Equal(
            "INR",
            result.Currency);

        Assert.Single(result.Items);

        Assert.Equal(
            "CONFLUX-GET-001",
            result.Items[0].Sku);

        Assert.Equal(
            3,
            result.Items[0].Quantity);

        Assert.Equal(
            75m,
            result.Items[0].UnitPrice);

        Assert.Equal(
            225m,
            result.Items[0].LineTotal);
    }

    /// <summary>
    /// Verifies that retrieving an unknown order identifier
    /// returns HTTP 404 Not Found.
    /// </summary>
    [Fact]
    public async Task GetOrder_WithUnknownOrder_ReturnsNotFound() {
        var response = await _client.GetAsync(
            $"/api/v1/orders/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    /// <summary>
    /// Sends an order creation request using the specified idempotency key.
    /// </summary>
    /// <param name="request">
    /// The order creation request.
    /// </param>
    /// <param name="idempotencyKey">
    /// The idempotency key associated with the request.
    /// </param>
    /// <returns>
    /// The HTTP response returned by the Order service.
    /// </returns>
    private async Task<HttpResponseMessage>
        SendCreateOrderRequestAsync(
            CreateOrderRequest request,
            string idempotencyKey) {
        using var httpRequest = CreateHttpRequest(
            request,
            idempotencyKey);

        return await _client.SendAsync(
            httpRequest,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Creates an HTTP request containing the order payload and idempotency key.
    /// </summary>
    /// <param name="request">
    /// The order creation request.
    /// </param>
    /// <param name="idempotencyKey">
    /// The idempotency key to add to the request.
    /// </param>
    /// <returns>
    /// A configured HTTP request message.
    /// </returns>
    private static HttpRequestMessage CreateHttpRequest(
        CreateOrderRequest request,
        string idempotencyKey) {
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/orders");

        httpRequest.Headers.Add(
            "Idempotency-Key",
            idempotencyKey);

        httpRequest.Content =
            JsonContent.Create(request);

        return httpRequest;
    }

    /// <summary>
    /// Creates a single-item order creation request for testing.
    /// </summary>
    /// <param name="customerId">
    /// The customer identifier.
    /// </param>
    /// <param name="sku">
    /// The stock keeping unit.
    /// </param>
    /// <param name="quantity">
    /// The quantity requested.
    /// </param>
    /// <param name="unitPrice">
    /// The unit price.
    /// </param>
    /// <param name="currency">
    /// The ISO 4217 currency code.
    /// </param>
    /// <returns>
    /// A populated order creation request.
    /// </returns>
    private static CreateOrderRequest CreateRequest(
        Guid customerId,
        string sku,
        int quantity,
        decimal unitPrice,
        string currency) {
        return new CreateOrderRequest
        {
            CustomerId = customerId,
            Items =
            [
                new CreateOrderItemRequest
                {
                    Sku = sku,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    Currency = currency
                }
            ]
        };
    }

    /// <summary>
    /// Represents an order creation request used by the integration tests.
    /// </summary>
    private sealed record CreateOrderRequest {
        /// <summary>
        /// Gets the customer identifier.
        /// </summary>
        public Guid CustomerId { get; init; }

        /// <summary>
        /// Gets the order items.
        /// </summary>
        public required IReadOnlyList<CreateOrderItemRequest> Items { get; init; }
    }

    /// <summary>
    /// Represents an order item in an integration-test request.
    /// </summary>
    private sealed record CreateOrderItemRequest {
        /// <summary>
        /// Gets the stock keeping unit.
        /// </summary>
        public required string Sku { get; init; }

        /// <summary>
        /// Gets the requested quantity.
        /// </summary>
        public int Quantity { get; init; }

        /// <summary>
        /// Gets the unit price.
        /// </summary>
        public decimal UnitPrice { get; init; }

        /// <summary>
        /// Gets the ISO 4217 currency code.
        /// </summary>
        public required string Currency { get; init; }
    }

    /// <summary>
    /// Represents the order response returned by the Order service.
    /// </summary>
    private sealed record CreateOrderResponse {
        /// <summary>
        /// Gets the order identifier.
        /// </summary>
        public required Guid OrderId { get; init; }

        /// <summary>
        /// Gets the customer identifier.
        /// </summary>
        public required Guid CustomerId { get; init; }

        /// <summary>
        /// Gets the current order status.
        /// </summary>
        public OrderStatus Status { get; init; }

        /// <summary>
        /// Gets the total order amount.
        /// </summary>
        public decimal TotalAmount { get; init; }

        /// <summary>
        /// Gets the ISO 4217 currency code.
        /// </summary>
        public required string Currency { get; init; }

        /// <summary>
        /// Gets the order items.
        /// </summary>
        public required IReadOnlyList<CreateOrderItemResponse> Items { get; init; }
    }

    /// <summary>
    /// Represents an order item returned by the Order service.
    /// </summary>
    private sealed record CreateOrderItemResponse {
        /// <summary>
        /// Gets the order item identifier.
        /// </summary>
        public required Guid ItemId { get; init; }

        /// <summary>
        /// Gets the stock keeping unit.
        /// </summary>
        public required string Sku { get; init; }

        /// <summary>
        /// Gets the ordered quantity.
        /// </summary>
        public int Quantity { get; init; }

        /// <summary>
        /// Gets the captured unit price.
        /// </summary>
        public decimal UnitPrice { get; init; }

        /// <summary>
        /// Gets the ISO 4217 currency code.
        /// </summary>
        public required string Currency { get; init; }

        /// <summary>
        /// Gets the total price of the order line.
        /// </summary>
        public decimal LineTotal { get; init; }
    }

    /// <summary>
    /// Represents an order response returned by the Get Order endpoint.
    /// </summary>
    private sealed record GetOrderResponse {
        /// <summary>
        /// Gets the order identifier.
        /// </summary>
        public required Guid OrderId { get; init; }

        /// <summary>
        /// Gets the customer identifier.
        /// </summary>
        public required Guid CustomerId { get; init; }

        /// <summary>
        /// Gets the current order status.
        /// </summary>
        public OrderStatus Status { get; init; }

        /// <summary>
        /// Gets the total order amount.
        /// </summary>
        public decimal TotalAmount { get; init; }

        /// <summary>
        /// Gets the ISO 4217 currency code.
        /// </summary>
        public required string Currency { get; init; }

        /// <summary>
        /// Gets the UTC timestamp at which the order was created.
        /// </summary>
        public DateTimeOffset CreatedAt { get; init; }

        /// <summary>
        /// Gets the UTC timestamp at which the order was last updated.
        /// </summary>
        public DateTimeOffset UpdatedAt { get; init; }

        /// <summary>
        /// Gets the order items.
        /// </summary>
        public required IReadOnlyList<GetOrderItemResponse> Items { get; init; }
    }

    /// <summary>
    /// Represents an order item returned by the Get Order endpoint.
    /// </summary>
    private sealed record GetOrderItemResponse {
        /// <summary>
        /// Gets the order item identifier.
        /// </summary>
        public required Guid ItemId { get; init; }

        /// <summary>
        /// Gets the stock keeping unit.
        /// </summary>
        public required string Sku { get; init; }

        /// <summary>
        /// Gets the ordered quantity.
        /// </summary>
        public int Quantity { get; init; }

        /// <summary>
        /// Gets the captured unit price.
        /// </summary>
        public decimal UnitPrice { get; init; }

        /// <summary>
        /// Gets the ISO 4217 currency code.
        /// </summary>
        public required string Currency { get; init; }

        /// <summary>
        /// Gets the total price of the order line.
        /// </summary>
        public decimal LineTotal { get; init; }
    }
}