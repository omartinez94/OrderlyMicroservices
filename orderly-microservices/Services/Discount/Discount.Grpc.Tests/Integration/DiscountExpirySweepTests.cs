using Discount.Grpc.Services;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Verifies <see cref="DiscountExpirySweepService"/> behaviour: disabled-flag
/// path is a no-op; pinned-now past expiry soft-deletes with
/// <see cref="DiscountActors.Sweep"/>; future-dated coupons are untouched.
/// </summary>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class DiscountExpirySweepTests(DiscountWebApplicationFactory factory)
{
    private static readonly Guid TenantGuid = new("cccccccc-0000-0000-0000-000000000001");

    [Fact]
    public async Task Disabled_NoOp_DoesNotSoftDelete()
    {
        await factory.CleanAllAsync();
        var nowUtc = DateTimeOffset.UtcNow;
        var pastExpiration = Instant.FromDateTimeUtc(nowUtc.AddDays(-1).UtcDateTime);

        var coupon = await factory.SeedCouponAsync(
            TenantGuid,
            code: "EXPIRED-BUT-DISABLED",
            expirationDate: pastExpiration);

        var sweep = BuildSweepService(enabled: false, now: nowUtc);
        await sweep.SweepNowForTestsAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var row = await db.Coupons.IgnoreQueryFilters()
            .FirstAsync(c => c.Id == coupon.Id);

        row.DeletedAt.Should().BeNull("a disabled-flag sweep never runs");
    }

    [Fact]
    public async Task PinnedNow_PastExpiry_SoftDeletesWithSweepActor()
    {
        await factory.CleanAllAsync();
        var nowUtc = DateTimeOffset.UtcNow;
        var pastExpiration = Instant.FromDateTimeUtc(nowUtc.AddDays(-1).UtcDateTime);

        var coupon = await factory.SeedCouponAsync(
            TenantGuid,
            code: "EXPIRED-COUPON",
            expirationDate: pastExpiration);

        var sweep = BuildSweepService(enabled: true, now: nowUtc);
        await sweep.SweepNowForTestsAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var row = await db.Coupons.IgnoreQueryFilters()
            .FirstAsync(c => c.Id == coupon.Id);

        row.DeletedAt.Should().NotBeNull("an expired coupon should be soft-deleted");
        row.DeletedBy.Should().Be(DiscountActors.Sweep,
            "the sweep host is the only actor that writes the Sweep constant");
    }

    [Fact]
    public async Task FutureDated_NotSwept()
    {
        await factory.CleanAllAsync();
        var nowUtc = DateTimeOffset.UtcNow;
        var futureExpiration = Instant.FromDateTimeUtc(nowUtc.AddDays(30).UtcDateTime);

        var coupon = await factory.SeedCouponAsync(
            TenantGuid,
            code: "FRESH-COUPON",
            expirationDate: futureExpiration);

        var sweep = BuildSweepService(enabled: true, now: nowUtc);
        await sweep.SweepNowForTestsAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var row = await db.Coupons.IgnoreQueryFilters()
            .FirstAsync(c => c.Id == coupon.Id);

        row.DeletedAt.Should().BeNull("a coupon that hasn't expired yet is not touched");
    }

    /// <summary>Builds a sweep service with the test's <see cref="TimeProvider"/>
    /// pinned to <paramref name="now"/>. Mirrors Catalog's manual
    /// <c>new ReservationReminderJob(... new TestTimeProvider(now) ...)</c>
    /// pattern (Catalog.API.Tests/Integration/ReservationReminderJobTests.cs)
    /// so the scheduled job tests don't depend on real wall-clock.</summary>
    private DiscountExpirySweepService BuildSweepService(bool enabled, DateTimeOffset now)
    {
        // The sweep service takes IServiceProvider and CreateScope()s
        // internally per iteration. Pass the test factory's services
        // — that's the shared app service provider.
        // Use the fully-qualified Microsoft.Extensions.Options.Options.Create
        // since `Options` collides with the Discount.Grpc.Options namespace
        // exposed by the global `using Discount.Grpc.Options;` import.
        var options = Microsoft.Extensions.Options.Options.Create(new DiscountExpirySweepOptions
        {
            Enabled = enabled,
            SweepInterval = TimeSpan.FromMinutes(5),
        });
        var clock = new TestTimeProvider(now);
        var logger = NullLogger<DiscountExpirySweepService>.Instance;

        return new DiscountExpirySweepService(factory.Services, clock, options, logger);
    }
}
