namespace Conflux.Identity.Features.Register;

/// <summary>
/// Represents the request payload for registering a new user.
/// </summary>
public sealed record RegisterRequest {
    /// <summary>
    /// Gets the email address for the new user.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Gets the plaintext password supplied during registration.
    /// </summary>
    public required string Password { get; init; }
}