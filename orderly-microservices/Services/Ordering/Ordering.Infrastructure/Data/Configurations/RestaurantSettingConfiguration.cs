namespace Ordering.Infrastructure.Data.Configurations;

public class RestaurantSettingConfiguration : IEntityTypeConfiguration<RestaurantSetting>
{
    public void Configure(EntityTypeBuilder<RestaurantSetting> builder)
    {
        builder.ToTable("RestaurantSettings");

        builder.HasKey(rs => rs.RestaurantId);

        builder.Property(rs => rs.RestaurantId)
            .ValueGeneratedNever();

        builder.Property(rs => rs.TimeZoneId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(rs => rs.UpdatedAt)
            .IsRequired();
    }
}
