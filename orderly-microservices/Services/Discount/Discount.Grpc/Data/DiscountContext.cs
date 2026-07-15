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

    /// <summary>Eligibility predicates attached to <see cref="Coupon"/> rows.
    /// One rule per coupon per UK on <c>(RestaurantId, CouponId)</c>.</summary>
    public DbSet<DiscountRule> DiscountRules { get; set; } = default!;

    /// <summary>Customer-feedback-generated rewards. UK on
    /// <c>(RestaurantId, Code)</c>; C# 11 <c>required</c> modifier on
    /// <c>Code</c> enforces non-null at construction. The deterministic
    /// <c>Code*Star*</c> helpers on <see cref="RewardCode"/> produce codes
    /// that collide on the same UK row across day boundaries when the
    /// <c>feedbackEventId</c> is identical — the natural-idempotency
    /// guarantee for the <c>FeedbackSubmittedConsumer</c> (Phase 5).</summary>
    public DbSet<RewardCode> RewardCodes { get; set; } = default!;

    /// <summary>Consumer-side idempotency log. Composite PK on
    /// <c>(EventId, ConsumerType)</c>; duplicate inserts from a bus
    /// redelivery hit the unique constraint and the consumer swallows
    /// the violation (unique-key dedup is the cheaper side of the
    /// handler's idempotency contract).</summary>
    public DbSet<ProcessedInboundevent> ProcessedInboundevents { get; set; } = default!;

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

        // RewardCode: same combined global query filter shape as Coupon —
        // alive + tenant-scoped. The sweep service uses IgnoreQueryFilters
        // to soft-delete across tenants on expiry.
        modelBuilder.Entity<RewardCode>().HasQueryFilter(r =>
            r.DeletedAt == null &&
            r.RestaurantId == _restaurantProvider.RestaurantId);

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

        modelBuilder.Entity<DiscountRule>(entity =>
        {
            // FK → Coupons. Restrict cascade (cascade-delete policy)
            // so an admin must delete the rule before deleting the coupon.
            entity.HasOne<Coupon>()
                .WithMany()
                .HasForeignKey(r => r.CouponId)
                .OnDelete(DeleteBehavior.Restrict);

            // UK (RestaurantId, CouponId) — one rule per coupon per tenant.
            // Matches the "one rule per coupon" invariant.
            entity.HasIndex(r => new { r.RestaurantId, r.CouponId })
                .IsUnique()
                .HasDatabaseName("ux_discount_rules_restaurant_coupon");

            // Practical filter indexes for the consumer's
            // RequiredMenuItemIds match query (filter by RestaurantId,
            // JSON-touched predicate). CouponId is the PK-side of the
            // match path; the consumer reads via JsonContains or
            // LIKE-on-JSON, both of which are SQLite-sequential without
            // a JSON1 extension — acceptable for traffic shape.
            entity.HasIndex(r => new { r.RestaurantId, r.IsActive })
                .HasDatabaseName("ix_discount_rules_restaurant_active");
        });

        modelBuilder.Entity<ProcessedInboundevent>(entity =>
        {
            // Composite PK on (EventId, ConsumerType) — the idempotency
            // key. Insertion race-resolution is enforced by the PK + the
            // handler's catch on SqliteException.SqlState == "1555"
            // (SQLITE_CONSTRAINT_PRIMARYKEY).
            entity.HasKey(p => new { p.EventId, p.ConsumerType });

            // Diagnostic index — operators may want to find all rows
            // consumed by a given consumer type ordered by time.
            entity.HasIndex(p => new { p.ConsumerType, p.ConsumedAt })
                .HasDatabaseName("ix_processed_inbound_consumer_time");
        });

        modelBuilder.Entity<RewardCode>(entity =>
        {
            // UK (RestaurantId, Code) — natural-key lookup per tenant
            // and the natural-idempotency collision target for the
            // Phase 5 FeedbackSubmittedConsumer (the deterministic
            // Code4StarPct10/Code5StarPct15/Code5StarAppetizer helpers
            // collide on the same UK row across day boundaries when the
            // inbound FeedbackSubmittedIntegrationEvent.Id is identical).
            entity.HasIndex(r => new { r.RestaurantId, r.Code })
                .IsUnique()
                .HasDatabaseName("ux_reward_codes_restaurant_code");

            // Practical filter index — sweep service scans by
            // (RestaurantId, IsActive, ExpirationDate). RestaurantId is
            // the leading column for index-only scans.
            entity.HasIndex(r => new { r.RestaurantId, r.IsActive, r.ExpirationDate })
                .HasDatabaseName("ix_reward_codes_restaurant_active_expiry");
        });
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
