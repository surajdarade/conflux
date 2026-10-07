using Conflux.Fulfillment.Domain;
using Conflux.Inbox;
using Microsoft.EntityFrameworkCore;

using FullfillentEntity = Conflux.Fulfillment.Domain.Fulfillment;

namespace Conflux.Fulfillment.Infrastructure;

/// <summary>Provides persistence for the Fulfillment service.</summary>
public sealed class FulfillmentDbContext : DbContext
{
    /// <summary>Initializes the database context.</summary>
    public FulfillmentDbContext(DbContextOptions<FulfillmentDbContext> options) : base(options) { }

    /// <summary>Gets fulfillments.</summary>
    public DbSet<FullfillentEntity> Fulfillments => Set<FullfillentEntity>();

    /// <summary>Gets received integration events.</summary>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FullfillentEntity>(entity =>
        {
            entity.ToTable("fulfillments");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.OrderId).IsUnique();
            entity.Property(item => item.CustomerId).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(item => item.CreatedAt).IsRequired();
            entity.Property(item => item.UpdatedAt).IsRequired();
        });

        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
    }
}
