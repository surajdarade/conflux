using System.Security.Claims;

namespace Conflux.Identity.Features.GetCurrentUser;

/// <summary>
/// Provides the HTTP endpoint for retrieving the currently authenticated user.
/// </summary>
public static class GetCurrentUserEndpoint {
    /// <summary>
    /// Maps the current-user endpoint to the application.
    /// </summary>
    /// <param name="endpoints">
    /// The endpoint route builder used to register the HTTP endpoint.
    /// </param>
    public static void MapGetCurrentUserEndpoint(
        this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet(
                "/api/v1/identity/me",
                Handle)
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithTags("Identity");
    }

    private static IResult Handle(ClaimsPrincipal principal) {
        var userIdClaim = principal.FindFirstValue(
            ClaimTypes.NameIdentifier);

        var emailClaim = principal.FindFirstValue(
            ClaimTypes.Email);

        if (!Guid.TryParse(userIdClaim, out var userId) ||
            string.IsNullOrWhiteSpace(emailClaim)) {
            return Results.Unauthorized();
        }

        var response = new GetCurrentUserResponse
        {
            UserId = userId,
            Email = emailClaim
        };

        return Results.Ok(response);
    }
}