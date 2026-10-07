using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conflux.Inbox;

/// <summary>Configures the database mapping for <see cref="InboxMessage"/>.</summary>
public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.ConsumerName).HasMaxLength(256).IsRequired();
        builder.Property(message => message.EventType).HasMaxLength(256).IsRequired();
        builder.Property(message => message.Payload).IsRequired();
        builder.Property(message => message.CorrelationId).IsRequired();
        builder.Property(message => message.CausationId);
        builder.Property(message => message.ReceivedAt).IsRequired();
        builder.Property(message => message.ProcessedAt);
        builder.Property(message => message.LastError).HasMaxLength(4000);
        builder.HasIndex(message => new { message.ConsumerName, message.Id }).IsUnique();
        builder.HasIndex(message => new { message.ConsumerName, message.ProcessedAt, message.ReceivedAt });
    }
}
