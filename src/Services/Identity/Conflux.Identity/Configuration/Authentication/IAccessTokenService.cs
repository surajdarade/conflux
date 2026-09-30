using Conflux.Identity.Domain;

namespace Conflux.Identity.Configuration.Authentication;

/// <summary>
/// Defines the service responsible for issuing authentication access tokens.
/// </summary>
public interface IAccessTokenService {
    /// <summary>
    /// Creates an access token for the specified user.
    /// </summary>
    /// <param name="user">
    /// The authenticated user for whom the token is issued.
    /// </param>
    /// <returns>
    /// A signed JWT access token.
    /// </returns>
    string CreateAccessToken(User user);
}