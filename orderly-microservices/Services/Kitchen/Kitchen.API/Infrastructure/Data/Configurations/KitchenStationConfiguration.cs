using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kitchen.API.Infrastructure.Data.Configurations;

public class KitchenStationConfiguration : IEntityTypeConfiguration<KitchenStation>
{
    public void Configure(EntityTypeBuilder<KitchenStation> builder)
    {
        builder.ToTable("kitchen_stations");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => StationId.Of(value))
            .HasColumnName("Id")
            .HasColumnType("uuid");

        builder.Property(s => s.RestaurantId).HasColumnName("RestaurantId").HasColumnType("uuid");
        builder.Property(s => s.Name).HasColumnName("Name").HasMaxLength(100).IsRequired();
        builder.Property(s => s.SortOrder).HasColumnName("SortOrder");
        builder.Property(s => s.IsActive).HasColumnName("IsActive");

        builder.HasIndex(s => new { s.RestaurantId, s.IsActive });
    }
}