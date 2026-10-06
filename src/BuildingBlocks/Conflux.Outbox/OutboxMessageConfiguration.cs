using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conflux.Outbox;

/// <summary>
/// Configures the database mapping for
/// <see cref="OutboxMessage"/>.
/// </summary>
public sealed class OutboxMessageConfiguration :
    IEntityTypeConfiguration<OutboxMessage> {
    /// <inheritdoc />
    public void Configure(
        EntityTypeBuilder<OutboxMessage> builder) {
        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.EventType)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(message => message.Payload)
            .IsRequired();

        builder.Property(message => message.CorrelationId)
            .IsRequired();

        builder.Property(message => message.CausationId);

        builder.Property(message => message.OccurredAt)
            .IsRequired();

        builder.Property(message => message.FirstAttemptedAt);

        builder.Property(message => message.PublishedAt);

        builder.Property(message => message.AttemptCount)
            .IsRequired();

        builder.Property(message => message.LastError)
            .HasMaxLength(4000);

        builder.HasIndex(message => new
        {
            message.PublishedAt,
            message.OccurredAt
        });

        builder.HasIndex(message => message.CorrelationId);
    }
}