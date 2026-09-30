using System.Net;
using System.Net.Http.Json;
using Conflux.Catalog.Features.Products.CreateProduct;
using Conflux.Catalog.Features.Products.GetProduct;
using Conflux.Catalog.Features.Products.ListProducts;
using Conflux.Catalog.IntegrationTests.Infrastructure;
using Xunit;

namespace Conflux.Catalog.IntegrationTests;

/// <summary>
/// Provides integration tests for the Catalog product endpoints.
/// </summary>
[Collection(CatalogTestCollection.Name)]
public sealed class CatalogEndpointsTests {
    private readonly CatalogApiFactory _factory;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogEndpointsTests"/> class.
    /// </summary>
    /// <param name="fixture">
    /// The shared Catalog integration-test infrastructure.
    /// </param>
    public CatalogEndpointsTests(CatalogTestFixture fixture) {
        _factory = new CatalogApiFactory(
            fixture.PostgresContainer);

        _client = _factory.CreateClient();

        _factory
            .ApplyDatabaseMigrationsAsync(
                TestContext.Current.CancellationToken)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Verifies that a valid product can be created.
    /// </summary>
    [Fact]
    public async Task CreateProduct_WithValidRequest_ReturnsCreated() {
        var request = CreateRequest();

        var response = await _client.PostAsJsonAsync(
            "/api/v1/catalog/products",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<CreateProductResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result!.ProductId);
        Assert.Equal(
            request.Sku.ToUpperInvariant(),
            result.Sku);
        Assert.Equal(request.Name, result.Name);
        Assert.Equal(request.Price, result.Price);
        Assert.Equal(
            request.Currency.ToUpperInvariant(),
            result.Currency);
        Assert.True(result.IsActive);
    }

    /// <summary>
    /// Verifies that duplicate SKUs are rejected.
    /// </summary>
    [Fact]
    public async Task CreateProduct_WithDuplicateSku_ReturnsConflict() {
        var request = CreateRequest();

        var firstResponse = await _client.PostAsJsonAsync(
            "/api/v1/catalog/products",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);

        var secondResponse = await _client.PostAsJsonAsync(
            "/api/v1/catalog/products",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode);
    }

    /// <summary>
    /// Verifies that a negative product price is rejected.
    /// </summary>
    [Fact]
    public async Task CreateProduct_WithNegativePrice_ReturnsBadRequest() {
        var request = CreateRequest() with
        {
            Price = -1
        };

        var response = await _client.PostAsJsonAsync(
            "/api/v1/catalog/products",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that an invalid currency code is rejected.
    /// </summary>
    [Fact]
    public async Task CreateProduct_WithInvalidCurrency_ReturnsBadRequest() {
        var request = CreateRequest() with
        {
            Currency = "US"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/v1/catalog/products",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that an existing product can be retrieved by identifier.
    /// </summary>
    [Fact]
    public async Task GetProduct_WithExistingProduct_ReturnsOk() {
        var createRequest = CreateRequest();

        var createResponse = await _client.PostAsJsonAsync(
            "/api/v1/catalog/products",
            createRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            createResponse.StatusCode);

        var createdProduct = await createResponse.Content
            .ReadFromJsonAsync<CreateProductResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(createdProduct);

        var response = await _client.GetAsync(
            $"/api/v1/catalog/products/{createdProduct!.ProductId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<GetProductResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(
            createdProduct.ProductId,
            result!.ProductId);
        Assert.Equal(
            createRequest.Sku.ToUpperInvariant(),
            result.Sku);
        Assert.Equal(createRequest.Name, result.Name);
        Assert.Equal(
            createRequest.Description,
            result.Description);
        Assert.Equal(createRequest.Price, result.Price);
        Assert.Equal(
            createRequest.Currency.ToUpperInvariant(),
            result.Currency);
        Assert.True(result.IsActive);
    }

    /// <summary>
    /// Verifies that retrieving an unknown product returns not found.
    /// </summary>
    [Fact]
    public async Task GetProduct_WithUnknownProduct_ReturnsNotFound() {
        var response = await _client.GetAsync(
            $"/api/v1/catalog/products/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that products can be retrieved using pagination.
    /// </summary>
    [Fact]
    public async Task ListProducts_WithPagination_ReturnsRequestedPage() {
        await CreateProductAsync();
        await CreateProductAsync();
        await CreateProductAsync();

        var response = await _client.GetAsync(
            "/api/v1/catalog/products?page=1&pageSize=2",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<ListProductsResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Products.Count);
        Assert.True(result.TotalCount >= 3);
        Assert.True(result.TotalPages >= 2);
    }

    /// <summary>
    /// Verifies that invalid pagination parameters are rejected.
    /// </summary>
    /// <param name="page">
    /// The page number supplied to the endpoint.
    /// </param>
    /// <param name="pageSize">
    /// The page size supplied to the endpoint.
    /// </param>
    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task ListProducts_WithInvalidPagination_ReturnsBadRequest(
        int page,
        int pageSize) {
        var response = await _client.GetAsync(
            $"/api/v1/catalog/products?page={page}&pageSize={pageSize}",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    private async Task<CreateProductResponse> CreateProductAsync() {
        var request = CreateRequest();

        var response = await _client.PostAsJsonAsync(
            "/api/v1/catalog/products",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<CreateProductResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        return result!;
    }

    private static CreateProductRequest CreateRequest() {
        return new CreateProductRequest
        {
            Sku = $"SKU-{Guid.NewGuid():N}",
            Name = "Conflux Test Product",
            Description = "Product created by Catalog integration tests.",
            Price = 99.99m,
            Currency = "USD"
        };
    }
}