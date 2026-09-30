using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Conflux.Identity.Features.Login;
using Conflux.Identity.Features.Register;
using Conflux.Identity.IntegrationTests.Infrastructure;
using Xunit;

namespace Conflux.Identity.IntegrationTests;

/// <summary>
/// Verifies the public HTTP behavior of the Identity service.
/// </summary>
[Collection(IdentityTestCollection.Name)]
public sealed class IdentityEndpointsTests : IAsyncLifetime
{
    private readonly IdentityTestFixture _fixture;
    private IdentityApiFactory _factory = null!;
    private HttpClient _client = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityEndpointsTests"/> class.
    /// </summary>
    /// <param name="fixture">
    /// The shared Identity integration-test fixture.
    /// </param>
    public IdentityEndpointsTests(IdentityTestFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Creates the Identity test application and applies its database migrations.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        _factory = new IdentityApiFactory(
            _fixture.PostgresContainer);

        _client = _factory.CreateClient();

        await _factory.ApplyDatabaseMigrationsAsync(
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Disposes the Identity test application.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>
    /// Verifies that registration creates a user and returns a created response.
    /// </summary>
    [Fact]
    public async Task Register_WithValidRequest_ReturnsCreated()
    {
        var email = CreateUniqueEmail();

        var request = new RegisterRequest
        {
            Email = email,
            Password = "Password123!"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/v1/identity/register",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<RegisterResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        Assert.NotEqual(
            Guid.Empty,
            result!.UserId);

        Assert.Equal(
            email,
            result.Email);
    }

    /// <summary>
    /// Verifies that registering an existing email returns a conflict response.
    /// </summary>
    [Fact]
    public async Task Register_WithExistingEmail_ReturnsConflict()
    {
        var email = CreateUniqueEmail();

        var request = new RegisterRequest
        {
            Email = email,
            Password = "Password123!"
        };

        var firstResponse = await _client.PostAsJsonAsync(
            "/api/v1/identity/register",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);

        var secondResponse = await _client.PostAsJsonAsync(
            "/api/v1/identity/register",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode);
    }

    /// <summary>
    /// Verifies that valid credentials produce a bearer access token.
    /// </summary>
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessToken()
    {
        var email = CreateUniqueEmail();

        await RegisterUserAsync(email);

        var request = new LoginRequest
        {
            Email = email,
            Password = "Password123!"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/v1/identity/login",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<LoginResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        Assert.False(
            string.IsNullOrWhiteSpace(result!.AccessToken));

        Assert.Equal(
            "Bearer",
            result.TokenType);

        Assert.Equal(
            1800,
            result.ExpiresIn);

        Assert.Equal(
            email,
            result.Email);

        Assert.NotEqual(
            Guid.Empty,
            result.UserId);
    }

    /// <summary>
    /// Verifies that an incorrect password cannot authenticate a user.
    /// </summary>
    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        var email = CreateUniqueEmail();

        await RegisterUserAsync(email);

        var request = new LoginRequest
        {
            Email = email,
            Password = "WrongPassword123!"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/v1/identity/login",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that an unknown email cannot authenticate a user.
    /// </summary>
    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        var request = new LoginRequest
        {
            Email = CreateUniqueEmail(),
            Password = "Password123!"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/v1/identity/login",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that the current-user endpoint requires authentication.
    /// </summary>
    [Fact]
    public async Task GetCurrentUser_WithoutAccessToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync(
            "/api/v1/identity/me",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    /// <summary>
    /// Verifies that a valid JWT authenticates the current-user endpoint.
    /// </summary>
    [Fact]
    public async Task GetCurrentUser_WithValidAccessToken_ReturnsCurrentUser()
    {
        var email = CreateUniqueEmail();

        await RegisterUserAsync(email);

        var loginRequest = new LoginRequest
        {
            Email = email,
            Password = "Password123!"
        };

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/v1/identity/login",
            loginRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);

        var loginResult = await loginResponse.Content
            .ReadFromJsonAsync<LoginResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(loginResult);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/me");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                loginResult!.AccessToken);

        var response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<CurrentUserResponse>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        Assert.Equal(
            loginResult.UserId,
            result!.UserId);

        Assert.Equal(
            email,
            result.Email);
    }

    private async Task RegisterUserAsync(string email)
    {
        var request = new RegisterRequest
        {
            Email = email,
            Password = "Password123!"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/v1/identity/register",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
    }

    private static string CreateUniqueEmail()
    {
        return $"integration-{Guid.NewGuid():N}@example.com";
    }

    private sealed record CurrentUserResponse
    {
        public required Guid UserId { get; init; }

        public required string Email { get; init; }
    }
}