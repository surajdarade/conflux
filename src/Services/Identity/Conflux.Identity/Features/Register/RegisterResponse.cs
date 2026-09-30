namespace Conflux.Identity.Features.Register;

/// <summary>
/// Represents the response returned after successful user registration.
/// </summary>
public sealed record RegisterResponse {
    /// <summary>
    /// Gets the unique identifier assigned to the newly registered user.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets the normalized email address associated with the new user.
    /// </summary>
    public required string Email { get; init; }
}