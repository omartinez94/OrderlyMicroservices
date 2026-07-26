using Marten.Schema;

namespace Basket.API.Models;

/// <summary>
/// Cross-account basket mutation audit row. Written by the Phase 4
/// admin endpoints (<c>PUT /api/v1/admin/carts/{userId}</c>,
/// <c>DELETE /api/v1/admin/carts/{userId}</c>) — the
/// <c>RestaurantSupportAgent</c> tool's actions leave a trace for
/// compliance review.
/// </summary>
/// <remarks>
/// <para>
/// A flat document (not a child of <see cref="Basket"/>) because
/// the audit row outlives the cart itself: a delete leaves a
/// basket-less row that names the deleted (user, restaurant) pair.
/// The (RestaurantId, OccurredAt) index supports a paged audit
/// query (newest-first) filtered to the active tenant.
/// </para>
/// <para>
/// <b>PII consideration.</b> The <see cref="ActorSub"/> +
/// <see cref="TargetUserId"/> are the only identity columns; the
/// cart body is NOT captured. The audit row is a fingerprint of
/// the action, not a content snapshot. A future
/// <c>audit_log_details</c> table can hold the body if compliance
/// needs it.
/// </para>
/// </remarks>
public class BasketAuditLogEntry
{
    /// <summary>Marten document identity (Guid).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Active tenant when the action ran. The query filter (set in
    /// <c>Program.cs</c>) restricts reads to the JWT's
    /// <c>restaurantId</c> claim.
    /// </summary>
    [Identity]
    public Guid RestaurantId { get; set; }

    /// <summary>
    /// The JWT subject of the actor that performed the action —
    /// <c>User.FindFirst(ClaimTypes.NameIdentifier)</c> on the
    /// inbound request. Empty for system-initiated sweeps (none of
    /// which write audit rows today; the admin path is the only
    /// writer).
    /// </summary>
    public string ActorSub { get; set; } = string.Empty;

    /// <summary>
    /// The <c>(userId, restaurantId)</c> pair the action targeted.
    /// <c>restaurantId</c> is duplicated as <see cref="RestaurantId"/>
    /// for index shape; we keep it on the row for readability.
    /// </summary>
    public Guid TargetUserId { get; set; }
    public Guid TargetRestaurantId { get; set; }

    /// <summary>
    /// Action verb — <c>AdminUpsert</c> or <c>AdminDelete</c>. The
    /// string form is intentional (no enum) — the audit table is
    /// read-mostly and the verb shape is the source of truth for
    /// downstream analytics.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Wall-clock timestamp (NodaTime Instant).</summary>
    public Instant OccurredAt { get; set; } = SystemClock.Instance.GetCurrentInstant();

    /// <summary>
    /// Optional free-form notes (e.g. CS ticket number, the reason
    /// the support agent recorded). Defaults to empty.
    /// </summary>
    public string Notes { get; set; } = string.Empty;
}
