using Conflux.Catalog.Domain;
using Conflux.Catalog.ReadModel;
using Conflux.Outbox;
using Conflux.Inbox;
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
    /// Gets the denormalized product read projection.
    /// </summary>
    public DbSet<ProductReadModel> ProductReadModels =>
        Set<ProductReadModel>();

    /// <summary>Gets durable integration events waiting to be published.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Gets durable received integration events.</summary>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

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

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        modelBuilder.Entity<ProductReadModel>(entity =>
        {
            entity.ToTable("product_read_models");
            entity.HasKey(product => product.ProductId);
            entity.Property(product => product.Sku).HasMaxLength(64).IsRequired();
            entity.HasIndex(product => product.Sku).IsUnique();
            entity.Property(product => product.Name).HasMaxLength(200).IsRequired();
            entity.Property(product => product.Description).HasMaxLength(4000).IsRequired();
            entity.Property(product => product.Price).HasPrecision(18, 2).IsRequired();
            entity.Property(product => product.Currency).HasMaxLength(3).IsRequired();
            entity.Property(product => product.IsActive).IsRequired();
            entity.Property(product => product.CreatedAt).IsRequired();
            entity.Property(product => product.UpdatedAt).IsRequired();
        });
    }
}