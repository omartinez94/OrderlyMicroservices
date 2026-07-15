using Discount.Grpc.Authorization;
using Discount.Grpc.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Discount.Grpc.Services;

/// <summary>
/// Background service that soft-deletes <see cref="Models.Coupon"/> and
/// <see cref="Models.RewardCode"/> rows whose <c>ExpirationDate</c> has
/// passed. Runs on a <see cref="PeriodicTimer"/> interval driven by
/// <see cref="DiscountExpirySweepOptions.SweepInterval"/> (default 5 minutes).
///
/// Soft-delete (not hard-delete) preserves the audit trail — redemption events
/// already cite coupon / reward-code Ids from finalized orders, and a row
/// can also be re-activated by clearing <c>DeletedAt</c>. The global query
/// filter excludes soft-deleted rows from every read path; admin tooling
/// can opt in via <c>IgnoreQueryFilters()</c> if it needs to see them.
///
/// Why a sweep rather than per-request filters: every Catalog / Basket read
/// would otherwise pay the filter cost on every lookup, and missed expiries
/// are silent (no error) — bad UX. A periodic sweep keeps the read path
/// tight and gives operators a single audit log (DeletedAt column) for
/// "why did this row stop being available?".
/// </summary>
public sealed class DiscountExpirySweepService(
    IServiceProvider services,
    TimeProvider clock,
    Microsoft.Extensions.Options.IOptions<DiscountExpirySweepOptions> options,
    Microsoft.Extensions.Logging.ILogger<DiscountExpirySweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Discount expiry sweep disabled via DiscountExpirySweepOptions.Enabled = false.");
            return;
        }

        logger.LogInformation(
            "Discount expiry sweep started. Interval {Interval}.",
            options.Value.SweepInterval);

        using var timer = new PeriodicTimer(options.Value.SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Don't crash the host on a single bad tick; log and try again.
                logger.LogError(ex, "Discount expiry sweep iteration failed.");
            }
        }

        logger.LogInformation("Discount expiry sweep stopped.");
    }

    /// <summary>
    /// Test-only seam: drives a single sweep iteration synchronously,
    /// honoring the <see cref="DiscountExpirySweepOptions.Enabled"/>
    /// flag (matches production <see cref="ExecuteAsync"/> semantics).
    /// Visible because <c>Discount.Grpc.Tests</c> is registered via
    /// <c>InternalsVisibleTo</c>; production code calls <c>ExecuteAsync</c>
    /// via the hosted-service loop. Tests then query the db for the
    /// post-sweep state.
    /// </summary>
    internal async Task SweepNowForTestsAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation(
                "SweepNowForTests skipped — DiscountExpirySweepOptions.Enabled = false.");
            return;
        }

        await SweepOnceAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        // Per-tick scope so a DbContext failure is contained. IgnoreQueryFilters
        // is intentional — we need to see expiring rows even if the calling
        // tenant would otherwise filter them out. The sweep operates across all
        // tenants; Coupon.RestaurantId / RewardCode.RestaurantId scoping is not
        // relevant for soft-delete.
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DiscountContext>();

        var now = Instant.FromDateTimeUtc(clock.GetUtcNow().UtcDateTime);

        // Coupon sweep — unchanged from Phase 1. Keeps the audit trail
        // (DeletedAt column) and lets admin tooling re-activate by clearing
        // DeletedAt if needed.
        var couponCandidates = await dbContext.Coupons
            .IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null && c.ExpirationDate != null && c.ExpirationDate <= now)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // RewardCode sweep — mirrors the Coupon path. Phase 3 adds this
        // aggregate; Phase 1's sweep needed an update.
        var rewardCodeCandidates = await dbContext.RewardCodes
            .IgnoreQueryFilters()
            .Where(r => r.DeletedAt == null && r.ExpirationDate != null && r.ExpirationDate <= now)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (couponCandidates.Count == 0 && rewardCodeCandidates.Count == 0)
        {
            return;
        }

        const string actor = DiscountActors.Sweep;
        foreach (var coupon in couponCandidates)
        {
            coupon.DeletedAt = now;
            coupon.DeletedBy = actor;
        }
        foreach (var rewardCode in rewardCodeCandidates)
        {
            rewardCode.DeletedAt = now;
            rewardCode.DeletedBy = actor;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Discount expiry sweep soft-deleted {CouponCount} expired coupon(s) and {RewardCount} expired reward code(s) at {Now}.",
            couponCandidates.Count,
            rewardCodeCandidates.Count,
            now);
    }
}

/// <summary>
/// Configuration knobs for <see cref="DiscountExpirySweepService"/>. Defaults
/// are tuned for low-traffic local dev (5 minutes — short enough to flush a
/// stale row before integration tests run, long enough to not hammer SQLite).
/// Production should crank the interval up (recommend 1 hour).
/// </summary>
public sealed class DiscountExpirySweepOptions
{
    public const string SectionName = "DiscountExpirySweep";

    /// <summary>Master switch. When false, the sweep never runs.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the sweep scans for expired coupons.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(5);
}
