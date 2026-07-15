using Discount.Grpc.Models;
using NodaTime;

namespace Discount.Grpc.Domain;

/// <summary>
/// Single canonical answer to "is this entity active right now?" — locked
/// signature per plan §0.4.3.1. Used by every read path
/// (<c>GetDiscount</c>, <c>GetRewardCode</c>, list RPCs' projected models),
/// by <c>RedeemDiscount</c> / <c>RedeemRewardCode</c> after the conditional
/// UPDATE succeeds, and by <c>DiscountExpirySweepService</c>. A divergent
/// copy in any handler is a code-review red flag.
/// </summary>
/// <remarks>
/// <see cref="DiscountRule"/> deliberately has no <c>RuleActiveNow</c>:
/// it has no <see cref="Coupon.ExpirationDate"/>, so rule activation is
/// the operator's responsibility (the sweep service does not deactivate
/// rules).
/// </remarks>
public static class ActiveNow
{
    /// <summary>Two-condition gate:
    /// <c>DeletedAt == null &amp;&amp; IsActive &amp;&amp; (ExpirationDate is null || ExpirationDate ≥ now)</c>.
    /// The soft-delete half is enforced by the global query filter on read
    /// paths; the helper re-applies it for in-memory results (post-fetch
    /// filter) so divergent copies can't drift.</summary>
    public static bool Coupon(Coupon c, TimeProvider clock)
        => c.DeletedAt == null
        && c.IsActive
        && (c.ExpirationDate is null || c.ExpirationDate >= Instant.FromDateTimeUtc(clock.GetUtcNow().UtcDateTime));

    /// <summary>Same two-condition gate as <see cref="Coupon"/>, mirrored
    /// on the <see cref="RewardCode"/> aggregate. Phase 3 introduces the
    /// helper; Phase 1's <c>Coupon.IsActiveNow</c> is migrated here
    /// (follow-up commit) so the two read paths converge on one shape.</summary>
    public static bool RewardCode(RewardCode r, TimeProvider clock)
        => r.DeletedAt == null
        && r.IsActive
        && (r.ExpirationDate is null || r.ExpirationDate >= Instant.FromDateTimeUtc(clock.GetUtcNow().UtcDateTime));
}