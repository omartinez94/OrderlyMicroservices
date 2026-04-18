using Discount.Grpc.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NodaTime;

namespace Discount.Grpc.Data;

public class DiscountContext(DbContextOptions<DiscountContext> options) : DbContext(options)
{
    public DbSet<Coupon> Coupons { get; set; } = default!;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<Instant>()
            .HaveConversion<InstantToLongConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Coupon>().HasData(
            new 
            {
                Id = 1,
                RestaurantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Code = "DISCOUNT10",
                Description = "10% off your order",
                Amount = 10m,
                RedeemAmount = 0,
                MaxRedeemAmount = 100,
                ExpirationDate = Instant.FromUtc(2024, 12, 31, 23, 59, 59),
                CreatedBy = "System",
                CreatedOn = Instant.FromUtc(2024, 1, 1, 0, 0, 0),
                LastModifiedBy = "System",
                IsActive = true
            },
            new 
            {
                Id = 2,
                RestaurantId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Code = "DISCOUNT20",
                Description = "20% off your order",
                Amount = 20m,
                RedeemAmount = 10,
                MaxRedeemAmount = 200,
                ExpirationDate = Instant.FromUtc(2024, 12, 31, 23, 59, 59),
                CreatedBy = "System",
                CreatedOn = Instant.FromUtc(2024, 1, 1, 0, 0, 0),
                LastModifiedBy = "System",
                IsActive = true
            }
        );
    }
}

public class InstantToLongConverter : ValueConverter<Instant, long>
{
    public InstantToLongConverter()
        : base(
            v => v.ToUnixTimeTicks(),
            v => Instant.FromUnixTimeTicks(v),
            null)
    {
    }
}
