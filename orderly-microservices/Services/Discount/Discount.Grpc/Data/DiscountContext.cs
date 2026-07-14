using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Multitenancy;
using Discount.Grpc.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NodaTime;

namespace Discount.Grpc.Data;

public class DiscountContext(
    DbContextOptions<DiscountContext> options,
    ICurrentRestaurantProvider restaurantProvider) : DbContext(options), IOutboxDbContext
{
    private readonly ICurrentRestaurantProvider _restaurantProvider = restaurantProvider;

    public DbSet<Coupon> Coupons { get; set; } = default!;

    /// <inheritdoc />
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = default!;

    /// <inheritdoc />
    public DbSet<OutboxDeadMessage> OutboxDeadMessages { get; set; } = default!;

    // <inheritdoc /> — inherited DbContext.Database satisfies IOutboxDbContext.Database.
    // Task<int> SaveChangesAsync(...) is inherited from DbContext and likewise satisfies
    // the interface; nothing to override here.

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<Instant>()
            .HaveConversion<InstantToLongConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Outbox tables: IEntityTypeConfiguration<T> implementations in
        // BuildingBlocks.Messaging.Outbox. Picking them up by assembly keeps
        // the schema identical across all adopter services.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OutboxMessage).Assembly);

        // Combined global query filter — every Coupon query is scoped to (a) the
        // requesting restaurant's GUID and (b) alive rows only (DeletedAt IS NULL).
        // Composed in one HasQueryFilter call because EF Core allows only one
        // filter per entity. Returns no rows when the provider can't resolve a
        // tenant (Guid.Empty can't match any row) — fail-secure default. The
        // DiscountExpirySweepService uses .IgnoreQueryFilters() to soft-delete
        // across all tenants regardless of the active caller.
        modelBuilder.Entity<Coupon>().HasQueryFilter(c =>
            c.DeletedAt == null &&
            c.RestaurantId == _restaurantProvider.RestaurantId);

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
                CreatedAt = Instant.FromUtc(2024, 1, 1, 0, 0, 0),
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
                CreatedAt = Instant.FromUtc(2024, 1, 1, 0, 0, 0),
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
