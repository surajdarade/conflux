using Conflux.Payment.Domain;
using Microsoft.EntityFrameworkCore;

using PaymentEntity = Conflux.Payment.Domain.Payment;

namespace Conflux.Payment.Infrastructure;

/// <summary>
/// Provides the Entity Framework Core database context for the Payment service.
/// </summary>
public sealed class PaymentDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentDbContext"/> class.
    /// </summary>
    /// <param name="options">
    /// The database context options.
    /// </param>
    public PaymentDbContext(
        DbContextOptions<PaymentDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets the payments managed by this context.
    /// </summary>
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentEntity>(entity =>
        {
            entity.ToTable("payments");

            entity.HasKey(payment => payment.Id);

            entity.Property(payment => payment.OrderId)
                .IsRequired();

            entity.HasIndex(payment => payment.OrderId);

            entity.Property(payment => payment.CustomerId)
                .IsRequired();

            entity.HasIndex(payment => payment.CustomerId);

            entity.Property(payment => payment.IdempotencyKey)
                .HasMaxLength(256)
                .IsRequired();

            entity.HasIndex(payment => payment.IdempotencyKey)
                .IsUnique();

            entity.Property(payment => payment.Amount)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(payment => payment.Currency)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(payment => payment.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(payment => payment.CreatedAt)
                .IsRequired();

            entity.Property(payment => payment.UpdatedAt)
                .IsRequired();
        });
    }
}