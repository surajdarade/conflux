using Conflux.Identity.Configuration.Authentication;
using Conflux.Identity.Domain;
using Conflux.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Conflux.Identity.Features.Login;

/// <summary>
/// Provides the HTTP endpoint for authenticating users.
/// </summary>
public static class LoginEndpoint {
    /// <summary>
    /// Maps the user login endpoint to the application.
    /// </summary>
    /// <param name="endpoints">
    /// The endpoint route builder used to register the HTTP endpoint.
    /// </param>
    public static void MapLoginEndpoint(this IEndpointRouteBuilder endpoints) {
        endpoints.MapPost(
                "/api/v1/identity/login",
                HandleAsync)
            .WithName("LoginUser")
            .WithTags("Identity");
    }

    private static async Task<IResult> HandleAsync(
        LoginRequest request,
        IdentityDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        IAccessTokenService accessTokenService,
        IOptions<JwtOptions> jwtOptions,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password)) {
            return Results.Unauthorized();
        }

        var email = request.Email.Trim().ToLowerInvariant();

        var user = await dbContext.Users
            .SingleOrDefaultAsync(
                candidate => candidate.Email == email,
                cancellationToken);

        if (user is null) {
            return Results.Unauthorized();
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (verificationResult == PasswordVerificationResult.Failed) {
            return Results.Unauthorized();
        }

        var accessToken = accessTokenService.CreateAccessToken(user);

        var expiresIn = checked(
            (int)TimeSpan.FromMinutes(
                jwtOptions.Value.AccessTokenLifetimeMinutes)
            .TotalSeconds);

        var response = new LoginResponse
        {
            AccessToken = accessToken,
            ExpiresIn = expiresIn,
            UserId = user.Id,
            Email = user.Email
        };

        return Results.Ok(response);
    }
}