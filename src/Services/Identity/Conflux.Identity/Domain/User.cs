namespace Conflux.Identity.Domain;

/// <summary>
/// Represents a user account managed by the Identity service.
/// </summary>
public sealed class User {
    private User() {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="User"/> class.
    /// </summary>
    /// <param name="id">
    /// The unique identifier of the user.
    /// </param>
    /// <param name="email">
    /// The normalized email address of the user.
    /// </param>
    /// <param name="passwordHash">
    /// The securely hashed password of the user.
    /// </param>
    public User(
        Guid id,
        string email,
        string passwordHash) {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the unique identifier of the user.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the normalized email address of the user.
    /// </summary>
    public string Email { get; private set; } = null!;

    /// <summary>
    /// Gets the securely hashed password of the user.
    /// </summary>
    public string PasswordHash { get; private set; } = null!;

    /// <summary>
    /// Gets the UTC timestamp at which the user account was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Replaces the current password hash with a newly generated hash.
    /// </summary>
    /// <param name="passwordHash">
    /// The securely generated password hash.
    /// </param>
    public void SetPasswordHash(string passwordHash) {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
    }
}