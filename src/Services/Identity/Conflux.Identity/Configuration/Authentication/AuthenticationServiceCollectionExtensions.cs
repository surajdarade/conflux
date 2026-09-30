using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Conflux.Identity.Configuration.Authentication;

/// <summary>
/// Provides dependency injection configuration for Identity authentication.
/// </summary>
public static class AuthenticationServiceCollectionExtensions {
    /// <summary>
    /// Registers JWT authentication and access-token services.
    /// </summary>
    /// <param name="services">
    /// The application service collection.
    /// </param>
    /// <param name="configuration">
    /// The application configuration.
    /// </param>
    /// <returns>
    /// The configured service collection.
    /// </returns>
    public static IServiceCollection AddConfluxAuthentication(
        this IServiceCollection services,
        IConfiguration configuration) {
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.Issuer) &&
                    !string.IsNullOrWhiteSpace(options.Audience) &&
                    !string.IsNullOrWhiteSpace(options.SigningKey) &&
                    options.SigningKey.Length >= 32 &&
                    options.AccessTokenLifetimeMinutes > 0,
                "JWT configuration is invalid.")
            .ValidateOnStart();

        services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();

        var jwtOptions = configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                "JWT configuration is missing.");

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,

                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,

                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization();

        return services;
    }
}