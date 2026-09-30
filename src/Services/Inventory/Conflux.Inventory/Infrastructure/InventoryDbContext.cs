using Conflux.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Inventory.Infrastructure;

/// <summary>
/// Provides the Entity Framework Core database context for the Inventory service.
/// </summary>
public sealed class InventoryDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InventoryDbContext"/> class.
    /// </summary>
    /// <param name="options">
    /// The options used to configure the database context.
    /// </param>
    public InventoryDbContext(
        DbContextOptions<InventoryDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets the inventory items managed by the Inventory service.
    /// </summary>
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    /// <summary>
    /// Gets the durable reservations managed by the Inventory service.
    /// </summary>
    public DbSet<InventoryReservation> InventoryReservations =>
        Set<InventoryReservation>();

    /// <summary>
    /// Configures the database model for the Inventory service.
    /// </summary>
    /// <param name="modelBuilder">
    /// The model builder used to configure entity mappings.
    /// </param>
    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("inventory_items");

            entity.HasKey(item => item.Id);

            entity.Property(item => item.Sku)
                .HasMaxLength(64)
                .IsRequired();

            entity.HasIndex(item => item.Sku)
                .IsUnique();

            entity.Property(item => item.AvailableQuantity)
                .IsRequired();

            entity.Property(item => item.ReservedQuantity)
                .IsRequired();

            entity.Property(item => item.CreatedAt)
                .IsRequired();

            entity.Property(item => item.UpdatedAt)
                .IsRequired();
        });

        modelBuilder.Entity<InventoryReservation>(entity =>
        {
            entity.ToTable("inventory_reservations");

            entity.HasKey(reservation => reservation.Id);

            entity.Property(reservation => reservation.ReservationId)
                .IsRequired();

            entity.HasIndex(reservation => reservation.ReservationId)
                .IsUnique();

            entity.Property(reservation => reservation.InventoryItemId)
                .IsRequired();

            entity.HasIndex(reservation => reservation.InventoryItemId);

            entity.Property(reservation => reservation.Quantity)
                .IsRequired();

            entity.Property(reservation => reservation.CreatedAt)
                .IsRequired();

            entity.Property(reservation => reservation.ReleasedAt);

            entity.HasOne<InventoryItem>()
                .WithMany()
                .HasForeignKey(reservation => reservation.InventoryItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}