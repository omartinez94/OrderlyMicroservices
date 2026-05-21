using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BuildingBlocks.Enums;

namespace Ordering.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => OrderId.Of(value));

        builder.HasMany(o => o.OrderItems)
            .WithOne()
            .HasForeignKey(oi => oi.OrderId);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(o => o.CustomerId)
            .IsRequired();

        builder.HasIndex(o => o.OrderNumber)
            .IsUnique();

        builder.ComplexProperty(o => o.OrderNumber, nameBuilder =>
        {
            nameBuilder.Property(on => on.Value)
                .HasMaxLength(100)
                .HasColumnName(nameof(Order.OrderNumber))
                .IsRequired();
        });

        builder.ComplexProperty(o => o.BillingAddress, addressBuilder =>
        {
            addressBuilder.Property(a => a.Street).HasMaxLength(180).IsRequired();
            addressBuilder.Property(a => a.City).HasMaxLength(50).IsRequired();
            addressBuilder.Property(a => a.State).HasMaxLength(50).IsRequired();
            addressBuilder.Property(a => a.ZipCode).HasMaxLength(5).IsRequired();
            addressBuilder.Property(a => a.Country).HasMaxLength(50).IsRequired();
        });

        builder.ComplexProperty(o => o.DeliveryAddress, addressBuilder =>
        {
            addressBuilder.Property(a => a.Street).HasMaxLength(180).IsRequired();
            addressBuilder.Property(a => a.City).HasMaxLength(50).IsRequired();
            addressBuilder.Property(a => a.State).HasMaxLength(50).IsRequired();
            addressBuilder.Property(a => a.ZipCode).HasMaxLength(5).IsRequired();
            addressBuilder.Property(a => a.Country).HasMaxLength(50).IsRequired();
        });

        builder.ComplexProperty(o => o.Payment, paymentBuilder =>
        {
            paymentBuilder.Property(p => p.CardName).HasMaxLength(50).IsRequired();
            paymentBuilder.Property(p => p.CardNumber).HasMaxLength(24).IsRequired();
            paymentBuilder.Property(p => p.Expiration).HasMaxLength(10).IsRequired();
            paymentBuilder.Property(p => p.CCV).HasMaxLength(3).IsRequired();
            paymentBuilder.Property(p => p.PaymentMethod).HasMaxLength(50).IsRequired();
        });

        builder.Property(o => o.Status)
            .HasDefaultValue(OrderStatus.Pending)
            .HasConversion(
                s => s.ToString(),
                s => (OrderStatus)Enum.Parse(typeof(OrderStatus), s));

        builder.Property(o => o.OrderType)
            .HasDefaultValue(OrderType.DineIn)
            .HasConversion(
                t => t.ToString(),
                t => (OrderType)Enum.Parse(typeof(OrderType), t));

        builder.Property(o => o.DeliveryStatus)
            .HasConversion(
                s => s == null ? null : s.ToString(),
                s => s == null ? null : (DeliveryStatus)Enum.Parse(typeof(DeliveryStatus), s));
    }
}
