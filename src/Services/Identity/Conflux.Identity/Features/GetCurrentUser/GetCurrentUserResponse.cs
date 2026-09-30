namespace Conflux.Identity.Features.GetCurrentUser;

/// <summary>
/// Represents the response returned for the currently authenticated user.
/// </summary>
public sealed record GetCurrentUserResponse {
    /// <summary>
    /// Gets the unique identifier of the authenticated user.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets the email address of the authenticated user.
    /// </summary>
    public required string Email { get; init; }
}