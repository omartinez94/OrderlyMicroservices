using BuildingBlocks.Entities.Contracts;
using BuildingBlocks.Multitenancy;
using NodaTime;

namespace Discount.Grpc.Models;

/// <summary>
/// Customer-feedback-generated reward. Issued by
/// <c>FeedbackSubmittedConsumer</c> (Phase 5 stub, disabled by default per
/// plan §7 Phase 5) and redeemed via <c>RedeemRewardCode</c>. Each row carries
/// a <see cref="Kind"/>-discriminated <see cref="Value"/> (see
/// <see cref="RewardKind"/>) and a tenant-unique <see cref="Code"/>.
/// </summary>
/// <remarks>
/// <para><b>Why <see cref="Value"/> is overloaded:</b> the four reward kinds
/// have semantically distinct payloads (percentage, currency, free-item target,
/// points count). A single column keeps the proto v1 simple and the
/// discriminator (<see cref="Kind"/>) keeps the shape stable; FluentValidation
/// enforces the kind-specific contract at the handler boundary per plan
/// §0.3.3. FreeItem rewards carry the target menu-item id in
/// <see cref="Description"/> as <c>free-item:{menuItemId}</c>; a future
/// <c>RewardTargetMenuItemId</c> field is a v2 proto bump per plan §0.4.3.</para>
/// <para><b>Why deterministic code builders:</b> the
/// <c>FeedbackSubmittedConsumer</c> path uses natural-idempotency via the
/// <c>Code</c> unique-key violation. The three <c>Code*Star*</c> helpers
/// combine <c>rid</c> + a kind tag + a day-bucket + the inbound event id so
/// a redelivery (same feedback id) collides on the same <c>Code</c> while
/// different feedback events land on different codes. The day-bucket is for
/// human-readable admin UIs; the event id is the actual idempotency key.
/// v1.2 H-L1 fixed the prior day-bucket-only scheme that broke idempotency
/// across midnight.</para>
/// </remarks>
public class RewardCode : AuditableEntity<int>, ITenantEntity
{
    /// <summary>Tenant scope; mirrors <see cref="Coupon.RestaurantId"/>.</summary>
    public Guid RestaurantId { get; set; }

    /// <summary>UK on <c>(RestaurantId, Code)</c>. C# 11 required modifier
    /// (v1.1 M7) — the column is non-nullable in the schema and the
    /// handler validates non-empty + ≤ 120 chars per §0.3.3.</summary>
    public required string Code { get; set; }

    /// <summary>Discriminator driving <see cref="Value"/>'s semantic shape.
    /// C# 11 required modifier (v1.1 M7).</summary>
    public required RewardKind Kind { get; set; }

    /// <summary>Overloaded payload — semantic depends on <see cref="Kind"/>:
    /// <list type="bullet">
    /// <item><see cref="RewardKind.Percentage"/>: <c>(0, 100]</c>.</item>
    /// <item><see cref="RewardKind.FixedAmount"/>: <c>&gt; 0</c> currency.</item>
    /// <item><see cref="RewardKind.FreeItem"/>: <c>0</c>; target menu-item
    /// id lives in <see cref="Description"/>.</item>
    /// <item><see cref="RewardKind.Points"/>: <c>&gt; 0</c> count.</item>
    /// </list>
    /// FluentValidation enforces the kind-specific contract.</summary>
    public decimal Value { get; set; }

    /// <summary>Free-text description. For <see cref="RewardKind.FreeItem"/>
    /// rewards, carries <c>free-item:{menuItemId}</c> as the target
    /// identifier.</summary>
    public string? Description { get; set; }

    /// <summary>Optional NodaTime <see cref="Instant"/> at which the code
    /// expires. <c>null</c> = no expiry. Validator requires
    /// <c>&gt; clock.GetCurrentInstant()</c> when set.</summary>
    public Instant? ExpirationDate { get; set; }

    /// <summary>Counter incremented by the atomic conditional-UPDATE
    /// path in <c>RedeemRewardCode</c>. Clamped against
    /// <see cref="MaxRedeemAmount"/>.</summary>
    public int RedeemAmount { get; set; }

    /// <summary>Optional cap on total redemptions. <c>null</c> = uncapped.</summary>
    public int? MaxRedeemAmount { get; set; }

    /// <summary>The order id that performed the most recent redemption.
    /// <c>null</c> until the first redemption. Set by the conditional
    /// UPDATE alongside <see cref="RedeemedAt"/>.</summary>
    public Guid? RedeemedInOrderId { get; set; }

    /// <summary>The instant of the most recent redemption. <c>null</c>
    /// until the first redemption.</summary>
    public Instant? RedeemedAt { get; set; }

    // Soft-delete columns — set by DiscountExpirySweepService when the code
    // expires. AuditableEntity.IsActive stays the business-flag (an admin
    // can deactivate a code pre-expiry); the sweep soft-deletes on expiry.
    // Both flag-gates participate in the global query filter — see
    // DiscountContext.OnModelCreating.
    public Instant? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    // ---------- Deterministic code builders (idempotent unique-key collision)
    //
    // The contract per plan §7 Phase 3 + §0.3.4: same (rid, feedbackEventId)
    // → same Code, regardless of when the consumer fires. Different feedback
    // events → different Codes. The day-bucket is the human-readable prefix
    // (audit reports group by date); the event id is the actual idempotency
    // key (collision target). The 120-char cap (validator) is generous; the
    // substring clamp below keeps us inside the schema boundary even when
    // a future change widens the prefix.
    //
    // Called by FeedbackSubmittedConsumer (Phase 5) when generating rewards
    // for a rating ≥ 4. The handler dispatches CreateRewardCodeCommand with
    // the helper's output as the Code field; a duplicate (bus redelivery)
    // hits the UK constraint and the duplicate-Create is swallowed.

    /// <summary>4★ rating → 10% off. Returns a deterministic code that
    /// collides on (rid, feedbackEventId) so bus redelivery is idempotent.</summary>
    internal static string Code4StarPct10(Guid rid, Guid feedbackEventId, TimeProvider clock)
        => BuildCode(rid, "4STAR-PCT10", feedbackEventId, clock);

    /// <summary>5★ rating → 15% off. Returns a deterministic code that
    /// collides on (rid, feedbackEventId) so bus redelivery is idempotent.</summary>
    internal static string Code5StarPct15(Guid rid, Guid feedbackEventId, TimeProvider clock)
        => BuildCode(rid, "5STAR-PCT15", feedbackEventId, clock);

    /// <summary>5★ rating → free appetizer. The target menu-item id is
    /// carried in the <c>Description</c> field by the consumer (see plan
    /// §7 Phase 5 for the wired-up shape).</summary>
    internal static string Code5StarAppetizer(Guid rid, Guid feedbackEventId, TimeProvider clock)
        => BuildCode(rid, "5STAR-APPETIZER", feedbackEventId, clock);

    private static string BuildCode(Guid rid, string tag, Guid feedbackEventId, TimeProvider clock)
    {
        // Day-prefix is human-readable (audit reports group by date);
        // the event-id suffix is the idempotency anchor. Both are
        // required: the day-prefix makes codes match in admin UIs, the
        // event-id makes them collide on the same UK row across day
        // boundaries.
        var day = clock.GetUtcNow().ToString("yyyyMMdd");
        var raw = $"RWD-{rid:N}-{tag}-{day}-{feedbackEventId:N}";
        // Validator caps Code at 120 chars; substring clamp is a
        // defense-in-depth guard so a future prefix-widening change
        // can't accidentally violate the schema.
        return raw[..Math.Min(120, raw.Length)];
    }
}