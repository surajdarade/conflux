using Conflux.Order.Domain;
using Microsoft.EntityFrameworkCore;
using OrderEntity = Conflux.Order.Domain.Order;

namespace Conflux.Order.Infrastructure;

/// <summary>
/// Provides the Entity Framework Core database context for the Order service.
/// </summary>
public sealed class OrderDbContext : DbContext {
    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="OrderDbContext"/> class.
    /// </summary>
    /// <param name="options">
    /// The options used to configure the database context.
    /// </param>
    public OrderDbContext(
        DbContextOptions<OrderDbContext> options)
        : base(options) {
    }

    /// <summary>
    /// Gets the orders managed by the Order service.
    /// </summary>
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();

    /// <summary>
    /// Gets the order items managed by the Order service.
    /// </summary>
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    /// <summary>
    /// Gets the durable inventory reservation correlations managed
    /// by the Order service.
    /// </summary>
    public DbSet<OrderInventoryReservation>
        OrderInventoryReservations =>
        Set<OrderInventoryReservation>();

    /// <summary>
    /// Configures the database model for the Order service.
    /// </summary>
    /// <param name="modelBuilder">
    /// The model builder used to configure entity mappings.
    /// </param>
    protected override void OnModelCreating(
        ModelBuilder modelBuilder) {
        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.ToTable("orders");

            entity.HasKey(order => order.Id);

            entity.Property(order => order.CustomerId)
                .IsRequired();

            entity.Property(order => order.IdempotencyKey)
                .HasMaxLength(256)
                .IsRequired();

            entity.HasIndex(order => order.IdempotencyKey)
                .IsUnique();

            entity.Property(order => order.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(order => order.TotalAmount)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(order => order.Currency)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(order => order.CreatedAt)
                .IsRequired();

            entity.Property(order => order.UpdatedAt)
                .IsRequired();

            entity.HasMany(order => order.Items)
                .WithOne()
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items");

            entity.HasKey(item => item.Id);

            entity.Property(item => item.Sku)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(item => item.Quantity)
                .IsRequired();

            entity.Property(item => item.UnitPrice)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(item => item.Currency)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(item => item.LineTotal)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(item => item.OrderId)
                .IsRequired();

            entity.HasIndex(item => item.OrderId);
        });

        modelBuilder.Entity<OrderInventoryReservation>(entity =>
        {
            entity.ToTable("order_inventory_reservations");

            entity.HasKey(reservation => reservation.Id);

            entity.Property(reservation => reservation.OrderItemId)
                .IsRequired();

            entity.HasIndex(reservation => reservation.OrderItemId)
                .IsUnique();

            entity.Property(reservation => reservation.InventoryItemId)
                .IsRequired();

            entity.HasIndex(reservation => reservation.InventoryItemId);

            entity.Property(reservation => reservation.ReservationId)
                .IsRequired();

            entity.HasIndex(reservation => reservation.ReservationId)
                .IsUnique();

            entity.Property(reservation => reservation.Quantity)
                .IsRequired();

            entity.Property(reservation => reservation.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(reservation => reservation.CreatedAt)
                .IsRequired();

            entity.Property(reservation => reservation.ReleasedAt);

            entity.HasOne<OrderItem>()
                .WithMany()
                .HasForeignKey(reservation => reservation.OrderItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}