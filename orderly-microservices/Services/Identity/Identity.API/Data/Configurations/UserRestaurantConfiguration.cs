namespace Identity.API.Data.Configurations;

public class UserRestaurantConfiguration : IEntityTypeConfiguration<UserRestaurant>
{
    public void Configure(EntityTypeBuilder<UserRestaurant> builder)
    {
        builder.ToTable("UserRestaurants");

        builder.HasKey(ur => new { ur.UserId, ur.RestaurantId });

        builder.HasOne(ur => ur.User)
            .WithMany()
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(ur => ur.IsDefault)
            .HasDefaultValue(false);
    }
}
