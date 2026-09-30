namespace Conflux.Identity.Features.Login;

/// <summary>
/// Represents the request payload for authenticating a user.
/// </summary>
public sealed record LoginRequest {
    /// <summary>
    /// Gets the email address supplied during authentication.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Gets the plaintext password supplied during authentication.
    /// </summary>
    public required string Password { get; init; }
}