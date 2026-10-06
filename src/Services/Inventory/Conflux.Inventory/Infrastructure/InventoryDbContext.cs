using Conflux.Inventory.Domain;
using Conflux.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Inventory.Infrastructure;

/// <summary>
/// Provides the Entity Framework Core database context for the Inventory service.
/// </summary>
public sealed class InventoryDbContext : DbContext {
    /// <summary>
    /// Initializes a new instance of the <see cref="InventoryDbContext"/> class.
    /// </summary>
    /// <param name="options">The database context options.</param>
    public InventoryDbContext(
        DbContextOptions<InventoryDbContext> options)
        : base(options) {
    }

    /// <summary>
    /// Gets the inventory items.
    /// </summary>
    public DbSet<InventoryItem> InventoryItems =>
        Set<InventoryItem>();

    /// <summary>
    /// Gets the inventory reservations.
    /// </summary>
    public DbSet<InventoryReservation> InventoryReservations =>
        Set<InventoryReservation>();

    /// <summary>
    /// Gets the durable integration events waiting to be published.
    /// </summary>
    public DbSet<OutboxMessage> OutboxMessages =>
        Set<OutboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder) {
        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("inventory_items");

            entity.HasKey(item => item.Id);

            entity.Property(item => item.Sku)
                .HasMaxLength(256)
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
        });

        modelBuilder.ApplyConfiguration(
            new OutboxMessageConfiguration());
    }
}