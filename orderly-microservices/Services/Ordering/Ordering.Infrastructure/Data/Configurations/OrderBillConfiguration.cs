namespace Ordering.Infrastructure.Data.Configurations;

public class OrderBillConfiguration : IEntityTypeConfiguration<OrderBill>
{
    public void Configure(EntityTypeBuilder<OrderBill> builder)
    {
        builder.HasKey(ob => ob.Id);
        
        builder.Property(ob => ob.Amount)
            .HasPrecision(18, 2);
            
        builder.Property(ob => ob.TaxAmount)
            .HasPrecision(18, 2);
            
        builder.Property(ob => ob.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(ob => ob.PaymentStatus)
            .HasDefaultValue(PaymentStatus.Pending)
            .HasConversion(
                s => s.ToString(),
                s => (PaymentStatus)Enum.Parse(typeof(PaymentStatus), s));

        builder.Property(ob => ob.SplitType)
            .HasDefaultValue(SplitType.Equal)
            .HasConversion(
                s => s.ToString(),
                s => (SplitType)Enum.Parse(typeof(SplitType), s));
    }
}
