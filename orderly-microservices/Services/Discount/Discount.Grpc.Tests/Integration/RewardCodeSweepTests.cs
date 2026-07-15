using Discount.Grpc.Data;
using Discount.Grpc.Models;
using Discount.Grpc.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Verifies the Phase 3 sweep extension: <see cref="DiscountExpirySweepService"/>
/// now soft-deletes expired <see cref="RewardCode"/> rows in addition to
/// expired <see cref="Coupon"/> rows. Plan §7 Phase 3 last bullet.
/// </summary>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class RewardCodeSweepTests(DiscountWebApplicationFactory factory)
{
    private static readonly Guid TenantGuid = new("dddddddd-0000-0000-0000-000000000003");

    [Fact]
    public async Task ExpiredRewardCode_IsSoftDeletedBySweep()
    {
        await factory.CleanAllAsync();

        // Seed a reward code with an expiration 1 hour in the past relative
        // to the test's pinned clock. The sweep should soft-delete it.
        var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var expiredAt = Instant.FromDateTimeUtc(now.UtcDateTime.AddHours(-1));

        await factory.SeedRewardCodeAsync(
            TenantGuid,
            code: "EXP-RWD",
            expirationDate: expiredAt);

        var sweep = BuildSweepService(enabled: true, now: now);
        await sweep.SweepNowForTestsAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var stored = await db.RewardCodes.IgnoreQueryFilters()
            .FirstAsync(r => r.Code == "EXP-RWD");

        stored.DeletedAt.Should().NotBeNull("an expired reward code must be soft-deleted");
        stored.DeletedBy.Should().Be(DiscountActors.Sweep,
            "the sweep actor string is reserved for sweep-driven soft-deletes");
    }

    [Fact]
    public async Task FutureRewardCode_IsNotTouchedBySweep()
    {
        await factory.CleanAllAsync();

        var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var futureExpiration = Instant.FromDateTimeUtc(now.UtcDateTime.AddHours(2));

        await factory.SeedRewardCodeAsync(
            TenantGuid,
            code: "FUT-RWD",
            expirationDate: futureExpiration);

        var sweep = BuildSweepService(enabled: true, now: now);
        await sweep.SweepNowForTestsAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var stored = await db.RewardCodes.IgnoreQueryFilters()
            .FirstAsync(r => r.Code == "FUT-RWD");

        stored.DeletedAt.Should().BeNull("a reward code that hasn't expired yet is not touched");
    }

    [Fact]
    public async Task DisabledSweep_DoesNothing()
    {
        await factory.CleanAllAsync();

        var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var expiredAt = Instant.FromDateTimeUtc(now.UtcDateTime.AddHours(-1));

        await factory.SeedRewardCodeAsync(
            TenantGuid,
            code: "DISABLED-RWD",
            expirationDate: expiredAt);

        var sweep = BuildSweepService(enabled: false, now: now);
        await sweep.SweepNowForTestsAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var stored = await db.RewardCodes.IgnoreQueryFilters()
            .FirstAsync(r => r.Code == "DISABLED-RWD");

        stored.DeletedAt.Should().BeNull("the disabled flag must short-circuit the sweep");
    }

    private DiscountExpirySweepService BuildSweepService(bool enabled, DateTimeOffset now)
    {
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