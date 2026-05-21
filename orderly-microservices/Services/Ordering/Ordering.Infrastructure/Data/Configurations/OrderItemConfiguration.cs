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
            .IsRequired();

        builder.Property(oi => oi.PrepStatus)
            .HasDefaultValue(PrepStatus.Pending)
            .HasConversion(
                s => s.ToString(),
                s => (PrepStatus)Enum.Parse(typeof(PrepStatus), s));
    }
}
