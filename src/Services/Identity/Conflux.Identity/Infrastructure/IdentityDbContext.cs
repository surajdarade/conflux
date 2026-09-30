using Conflux.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Identity.Infrastructure;

/// <summary>
/// Provides the Entity Framework Core database context for the Identity service.
/// </summary>
public sealed class IdentityDbContext : DbContext {
    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityDbContext"/> class.
    /// </summary>
    /// <param name="options">
    /// The options used to configure the database context.
    /// </param>
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options) {
    }

    /// <summary>
    /// Gets the users managed by the Identity service.
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// Configures the database model for the Identity service.
    /// </summary>
    /// <param name="modelBuilder">
    /// The model builder used to configure entity mappings.
    /// </param>
    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.HasKey(user => user.Id);

            entity.Property(user => user.Email)
                .HasMaxLength(320)
                .IsRequired();

            entity.HasIndex(user => user.Email)
                .IsUnique();

            entity.Property(user => user.PasswordHash)
                .IsRequired();

            entity.Property(user => user.CreatedAt)
                .IsRequired();
        });
    }
}