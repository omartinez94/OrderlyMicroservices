namespace Ordering.Infrastructure.Data.Configurations;

public class RestaurantDailySequenceConfiguration : IEntityTypeConfiguration<RestaurantDailySequence>
{
    public void Configure(EntityTypeBuilder<RestaurantDailySequence> builder)
    {
        builder.ToTable("RestaurantDailySequences");

        builder.HasKey(s => new { s.RestaurantId, s.BusinessDate });

        builder.Property(s => s.RestaurantId)
            .IsRequired();

        builder.Property(s => s.BusinessDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(s => s.CurrentValue)
            .IsRequired();
    }
}
