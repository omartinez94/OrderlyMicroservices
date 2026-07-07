using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// EF Core configuration for <see cref="OutboxMessage"/>. Shared across
/// services so the <c>outbox_messages</c> table has the same shape
/// regardless of which service hosts the dispatcher.
/// </summary>
public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

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

        builder.Property(m => m.DispatchedAt)
            .HasConversion(new ValueConverter<Instant?, DateTime?>(
                v => v.HasValue ? v.Value.ToDateTimeUtc() : null,
                v => v.HasValue
                    ? Instant.FromDateTimeUtc(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
                    : (Instant?)null));

        builder.Property(m => m.SchemaVersion)
            .IsRequired();

        // Dispatcher scans by (DispatchedAt IS NULL, OccurredOn) every tick;
        // this index keeps that scan cheap as the table grows.
        builder.HasIndex(m => new { m.DispatchedAt, m.OccurredOn })
            .HasDatabaseName("ix_outbox_messages_dispatched_at_occurred_on");
    }
}

/// <summary>
/// <see cref="Instant"/> <c>-&gt;</c> <see cref="DateTime"/> converter for
/// the outbox. Each service already has its own <c>InstantConverter</c>; we
/// keep this one isolated so the outbox table never accidentally inherits
/// a per-service shape.
/// </summary>
internal class OutboxInstantConverter : ValueConverter<Instant, DateTime>
{
    public OutboxInstantConverter()
        : base(
            v => v.ToDateTimeUtc(),
            v => Instant.FromDateTimeUtc(DateTime.SpecifyKind(v, DateTimeKind.Utc)))
    {
    }
}