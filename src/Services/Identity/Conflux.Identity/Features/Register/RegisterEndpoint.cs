using Conflux.Identity.Domain;
using Conflux.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Identity.Features.Register;

/// <summary>
/// Provides the HTTP endpoint for registering a new user.
/// </summary>
public static class RegisterEndpoint {
    /// <summary>
    /// Maps the user registration endpoint to the application.
    /// </summary>
    /// <param name="endpoints">
    /// The endpoint route builder used to register the HTTP endpoint.
    /// </param>
    public static void MapRegisterEndpoint(this IEndpointRouteBuilder endpoints) {
        endpoints.MapPost(
                "/api/v1/identity/register",
                HandleAsync)
            .WithName("RegisterUser")
            .WithTags("Identity");
    }

    private static async Task<IResult> HandleAsync(
        RegisterRequest request,
        IdentityDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(request.Email)) {
            return Results.BadRequest(
                new
                {
                    error = "Email is required."
                });
        }

        if (string.IsNullOrWhiteSpace(request.Password)) {
            return Results.BadRequest(
                new
                {
                    error = "Password is required."
                });
        }

        if (request.Password.Length < 8) {
            return Results.BadRequest(
                new
                {
                    error = "Password must contain at least 8 characters."
                });
        }

        var email = request.Email.Trim().ToLowerInvariant();

        var emailExists = await dbContext.Users
            .AnyAsync(
                user => user.Email == email,
                cancellationToken);

        if (emailExists) {
            return Results.Conflict(
                new
                {
                    error = "A user with this email address already exists."
                });
        }

        var user = new User(
            Guid.NewGuid(),
            email,
            string.Empty);

        var passwordHash = passwordHasher.HashPassword(
            user,
            request.Password);

        user.SetPasswordHash(passwordHash);

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new RegisterResponse
        {
            UserId = user.Id,
            Email = user.Email
        };

        return Results.Created(
            $"/api/v1/identity/users/{user.Id}",
            response);
    }
}