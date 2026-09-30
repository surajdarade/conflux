namespace Conflux.Identity.Configuration.Authentication;

/// <summary>
/// Defines the configuration required to issue and validate JWT access tokens.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// Gets the configuration section name used by the JWT options.
    /// </summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// Gets or sets the token issuer.
    /// </summary>
    public required string Issuer { get; set; }

    /// <summary>
    /// Gets or sets the token audience.
    /// </summary>
    public required string Audience { get; set; }

    /// <summary>
    /// Gets or sets the signing key used to sign JWT access tokens.
    /// </summary>
    public required string SigningKey { get; set; }

    /// <summary>
    /// Gets or sets the lifetime of an access token.
    /// </summary>
    public int AccessTokenLifetimeMinutes { get; set; }
}