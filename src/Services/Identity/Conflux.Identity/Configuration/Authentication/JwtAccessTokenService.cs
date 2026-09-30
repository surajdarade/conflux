using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Conflux.Identity.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Conflux.Identity.Configuration.Authentication;

/// <summary>
/// Creates signed JWT access tokens for authenticated users.
/// </summary>
public sealed class JwtAccessTokenService : IAccessTokenService {
    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtAccessTokenService"/> class.
    /// </summary>
    /// <param name="options">
    /// The JWT configuration options.
    /// </param>
    public JwtAccessTokenService(IOptions<JwtOptions> options) {
        _options = options.Value;

        var securityKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_options.SigningKey));

        _signingCredentials = new SigningCredentials(
            securityKey,
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public string CreateAccessToken(User user) {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(
                _options.AccessTokenLifetimeMinutes),
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}