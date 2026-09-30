using Conflux.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Catalog.Infrastructure;

/// <summary>
/// Provides the Entity Framework Core database context for the Catalog service.
/// </summary>
public sealed class CatalogDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogDbContext"/> class.
    /// </summary>
    /// <param name="options">
    /// The options used to configure the database context.
    /// </param>
    public CatalogDbContext(
        DbContextOptions<CatalogDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets the products managed by the Catalog service.
    /// </summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>
    /// Configures the database model for the Catalog service.
    /// </summary>
    /// <param name="modelBuilder">
    /// The model builder used to configure entity mappings.
    /// </param>
    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");

            entity.HasKey(product => product.Id);

            entity.Property(product => product.Sku)
                .HasMaxLength(64)
                .IsRequired();

            entity.HasIndex(product => product.Sku)
                .IsUnique();

            entity.Property(product => product.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(product => product.Description)
                .HasMaxLength(4000)
                .IsRequired();

            entity.Property(product => product.Price)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(product => product.Currency)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(product => product.IsActive)
                .IsRequired();

            entity.Property(product => product.CreatedAt)
                .IsRequired();

            entity.Property(product => product.UpdatedAt)
                .IsRequired();
        });
    }
}