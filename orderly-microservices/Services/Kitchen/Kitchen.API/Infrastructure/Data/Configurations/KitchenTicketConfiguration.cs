using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kitchen.API.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for <c>KitchenTicket</c>. Maps the aggregate's
/// private <c>_items</c> field to the <c>kitchen_ticket_items</c> child
/// table and uses PascalCase column names to match the rest of the
/// database (per project convention).
/// </summary>
public class KitchenTicketConfiguration : IEntityTypeConfiguration<KitchenTicket>
{
    public void Configure(EntityTypeBuilder<KitchenTicket> builder)
    {
        builder.ToTable("kitchen_tickets");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasConversion(id => id.Value, value => KitchenTicketId.Of(value))
            .HasColumnName("Id")
            .HasColumnType("uuid");

        builder.Property(t => t.RestaurantId).HasColumnName("RestaurantId").HasColumnType("uuid");
        builder.Property(t => t.CustomerId).HasColumnName("CustomerId").HasColumnType("uuid");
        builder.Property(t => t.OrderNumber).HasColumnName("OrderNumber").HasMaxLength(64).IsRequired();
        builder.Property(t => t.Status)
            .HasColumnName("Status")
            .HasConversion<int>()
            .HasColumnType("integer");
        builder.Property(t => t.ReceivedAt).HasColumnName("ReceivedAt").HasColumnType("timestamp with time zone");
        builder.Property(t => t.StartedAt).HasColumnName("StartedAt").HasColumnType("timestamp with time zone");
        builder.Property(t => t.ReadyAt).HasColumnName("ReadyAt").HasColumnType("timestamp with time zone");
        builder.Property(t => t.BumpedAt).HasColumnName("BumpedAt").HasColumnType("timestamp with time zone");
        builder.Property(t => t.CancelledAt).HasColumnName("CancelledAt").HasColumnType("timestamp with time zone");
        builder.Property(t => t.ConfirmedByUserId).HasColumnName("ConfirmedByUserId").HasColumnType("uuid");
        builder.Property(t => t.CancelledByUserId).HasColumnName("CancelledByUserId").HasColumnType("uuid");
        builder.Property(t => t.CancellationReason).HasColumnName("CancellationReason").HasMaxLength(500);
        builder.Property(t => t.Notes).HasColumnName("Notes").HasMaxLength(2000);

        // Domain events are dispatched by the interceptor — never persisted.
        builder.Ignore(t => t.DomainEvents);

        builder.OwnsMany(t => t.Items, item =>
        {
            item.ToTable("kitchen_ticket_items");
            item.WithOwner().HasForeignKey("KitchenTicketId");
            item.HasKey(i => i.Id);

            item.Property(i => i.Id)
                .HasConversion(id => id.Value, value => KitchenItemId.Of(value))
                .HasColumnName("Id")
                .HasColumnType("uuid");

            item.Property(i => i.OrderItemId).HasColumnName("OrderItemId").HasColumnType("uuid");
            item.Property(i => i.MenuItemId).HasColumnName("MenuItemId").HasColumnType("uuid");
            item.Property(i => i.MenuItemName).HasColumnName("MenuItemName").HasMaxLength(200).IsRequired();
            item.Property(i => i.Quantity).HasColumnName("Quantity");
            item.Property(i => i.UnitPrice).HasColumnName("UnitPrice").HasColumnType("numeric(12,2)");
            item.Property(i => i.SelectedVariations)
                .HasColumnName("SelectedVariations")
                .HasColumnType("text[]")
                .HasConversion(
                    v => v.ToArray(),
                    v => v.ToList())
                .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                    v => (IReadOnlyList<string>)v.ToList()));
            item.Property(i => i.Customizations)
                .HasColumnName("Customizations")
                .HasColumnType("text[]")
                .HasConversion(
                    v => v.ToArray(),
                    v => v.ToList())
                .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                    v => (IReadOnlyList<string>)v.ToList()));
            item.Property(i => i.SpecialInstructions).HasColumnName("SpecialInstructions").HasMaxLength(1000);
            item.Property(i => i.SeatNumber).HasColumnName("SeatNumber");
            item.Property(i => i.Status)
                .HasColumnName("Status")
                .HasConversion<int>()
                .HasColumnType("integer");
            item.Property(i => i.StartedAt).HasColumnName("StartedAt").HasColumnType("timestamp with time zone");
            item.Property(i => i.ReadyAt).HasColumnName("ReadyAt").HasColumnType("timestamp with time zone");
            item.Property(i => i.StationId).HasColumnName("StationId").HasColumnType("uuid");
        });

        builder.HasIndex(t => t.RestaurantId);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.ReceivedAt);
    }
}