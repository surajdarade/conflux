namespace Conflux.Identity.Features.Login;

/// <summary>
/// Represents the response returned after successful authentication.
/// </summary>
public sealed record LoginResponse {
    /// <summary>
    /// Gets the JWT access token issued to the authenticated user.
    /// </summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// Gets the token type.
    /// </summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// Gets the lifetime of the access token in seconds.
    /// </summary>
    public required int ExpiresIn { get; init; }

    /// <summary>
    /// Gets the unique identifier of the authenticated user.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets the normalized email address of the authenticated user.
    /// </summary>
    public required string Email { get; init; }
}