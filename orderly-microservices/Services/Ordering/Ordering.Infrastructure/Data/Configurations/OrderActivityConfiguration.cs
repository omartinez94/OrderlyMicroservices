using System.Text.Json;
using Ordering.Infrastructure.Serialization;

namespace Ordering.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core mapping for the <c>order_activities</c> child table. Mirrors
/// the <see cref="OrderItemConfiguration"/> jsonb pattern (cached
/// <c>JsonSerializerOptions</c> via <see cref="OrderActivityJson"/>).
/// </summary>
/// <remarks>
/// The aggregate navigation (<c>Order.Activities</c>) is wired in
/// <see cref="OrderConfiguration"/> via
/// <c>HasMany(o =&gt; o.Activities).WithOne().HasForeignKey(a =&gt; a.OrderId).OnDelete(DeleteBehavior.Cascade)</c>.
/// No <c>DbSet&lt;OrderActivity&gt;</c> is exposed on
/// <c>IApplicationDbContext</c> per the activity-feed plan §0.3 —
/// access goes through the <c>Order</c> aggregate.
/// </remarks>
public class OrderActivityConfiguration : IEntityTypeConfiguration<OrderActivity>
{
    public void Configure(EntityTypeBuilder<OrderActivity> builder)
    {
        builder.ToTable("order_activities");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion(id => id.Value, value => OrderActivityId.Of(value));

        builder.Property(a => a.OrderId)
            .HasConversion(id => id.Value, value => OrderId.Of(value))
            .IsRequired();

        builder.Property(a => a.ActivityType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.OccurredAt)
            .IsRequired();

        builder.Property(a => a.CorrelationId)
            .HasMaxLength(100);

        builder.Property(a => a.Notes)
            .HasMaxLength(2000);

        // Typed jsonb payload: OrderActivityMetadata record serialised
        // through the shared OrderActivityJson.Options so enum values
        // are stored as strings ("Confirmed", not 2).
        builder.Property(a => a.Metadata)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                m => m == null ? null : JsonSerializer.Serialize(m, OrderActivityJson.Options),
                v => v == null
                    ? null
                    : JsonSerializer.Deserialize<OrderActivityMetadata>(v, OrderActivityJson.Options));

        // Covering index for the read pattern:
        //   WHERE OrderId = @id ORDER BY OccurredAt ASC
        // Avoids the table scan + sort. Do not drop without re-measuring.
        builder.HasIndex(a => new { a.OrderId, a.OccurredAt })
            .HasDatabaseName("IX_order_activities_OrderId_OccurredAt");
    }
}