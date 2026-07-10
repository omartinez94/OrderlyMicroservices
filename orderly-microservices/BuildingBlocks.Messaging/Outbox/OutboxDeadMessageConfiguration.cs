using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// EF Core configuration for <see cref="OutboxDeadMessage"/>. Mirrors
/// <see cref="OutboxMessageConfiguration"/>'s shape so the
/// <c>outbox_messages_dead</c> table is a drop-in replacement for the
/// live <c>outbox_messages</c> table; the operator can re-emit a row by
/// copy-paste between the two once the underlying issue is resolved.
/// </summary>
public class OutboxDeadMessageConfiguration : IEntityTypeConfiguration<OutboxDeadMessage>
{
    public void Configure(EntityTypeBuilder<OutboxDeadMessage> builder)
    {
        builder.ToTable("outbox_messages_dead");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.OccurredOn)
            .HasConversion<OutboxInstantConverter>()
            .IsRequired();

        builder.Property(m => m.Type)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(m => m.Payload)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(m => m.SchemaVersion)
            .IsRequired();

        builder.Property(m => m.Reason)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(m => m.RejectedAt)
            .HasConversion(new ValueConverter<Instant, DateTime>(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(DateTime.SpecifyKind(v, DateTimeKind.Utc))))
            .IsRequired();

        // Triage query: "what died recently?" — index on RejectedAt.
        builder.HasIndex(m => m.RejectedAt)
            .HasDatabaseName("ix_outbox_messages_dead_rejected_at");
    }
}
