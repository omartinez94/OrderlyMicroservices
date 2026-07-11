using System.Text.Json;
using BuildingBlocks.Messaging.Events;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ordering.Infrastructure.Data.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(oi => oi.Id);
        builder.Property(oi => oi.Id)
            .HasConversion(id => id.Value, value => OrderItemId.Of(value));

        builder.HasOne<MenuItem>()
            .WithMany()
            .HasForeignKey(oi => oi.MenuItemId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(oi => oi.Quantity)
            .IsRequired();

        builder.Property(oi => oi.UnitPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(oi => oi.BasePrice)
            .HasPrecision(18, 2);

        builder.Property(oi => oi.TotalPrice)
            .HasPrecision(18, 2);

        builder.Property(oi => oi.PrepStatus)
            .HasDefaultValue(PrepStatus.Pending)
            .HasConversion(
                s => s.ToString(),
                s => (PrepStatus)Enum.Parse(typeof(PrepStatus), s));

        // Typed jsonb columns. The on-disk shape stays
        // nvarchar(max) jsonb — only the .NET property type changes from
        // string to IReadOnlyList<>. EF Core serialises the typed array
        // through System.Text.Json so the jsonb-parse path in
        // OrderExtensions is no longer needed. A row that holds
        // the legacy `["Size: Large"]` shape deserialises to an empty
        // list — those legacy entries are dropped at read time (the
        // basket/checkout payload already carries typed records, so
        // there is no legacy data flowing in via the wire today).
        builder.Property(oi => oi.SelectedVariations)
            .HasColumnType("nvarchar(max)")
            .IsRequired()
            .HasConversion(
                new ValueConverter<IReadOnlyList<KitchenOrderItemVariation>, string>(
                    v => JsonSerializer.Serialize(v ?? Array.Empty<KitchenOrderItemVariation>()),
                    v => DeserializeVariations(v)),
                new ValueComparer<IReadOnlyList<KitchenOrderItemVariation>>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(0, (acc, x) => HashCode.Combine(acc, x.GetHashCode())),
                    v => v.ToList()));

        builder.Property(oi => oi.Customizations)
            .HasColumnType("nvarchar(max)")
            .IsRequired()
            .HasConversion(
                new ValueConverter<IReadOnlyList<KitchenOrderItemCustomization>, string>(
                    v => JsonSerializer.Serialize(v ?? Array.Empty<KitchenOrderItemCustomization>()),
                    v => DeserializeCustomizations(v)),
                new ValueComparer<IReadOnlyList<KitchenOrderItemCustomization>>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(0, (acc, x) => HashCode.Combine(acc, x.GetHashCode())),
                    v => v.ToList()));
    }

    private static IReadOnlyList<KitchenOrderItemVariation> DeserializeVariations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<KitchenOrderItemVariation>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<KitchenOrderItemVariation>>(json)
                ?? new List<KitchenOrderItemVariation>();
        }
        catch (JsonException)
        {
            return Array.Empty<KitchenOrderItemVariation>();
        }
    }

    private static IReadOnlyList<KitchenOrderItemCustomization> DeserializeCustomizations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<KitchenOrderItemCustomization>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<KitchenOrderItemCustomization>>(json)
                ?? new List<KitchenOrderItemCustomization>();
        }
        catch (JsonException)
        {
            return Array.Empty<KitchenOrderItemCustomization>();
        }
    }
}