using BuildingBlocks.Entities.Contracts;
using BuildingBlocks.Multitenancy;
using NodaTime;

namespace Discount.Grpc.Models;

/// <summary>
/// Engine-side eligibility predicate attached to a <see cref="Coupon"/>.
/// Per plan §7 Phase 2 the engine shape is fixed: one rule per coupon
/// (UK on <c>(RestaurantId, CouponId)</c>) and <see cref="RuleDataJson"/>
/// carries the type-specific payload keyed by <see cref="RuleType"/>.
/// </summary>
/// <remarks>
/// <para>Why JSON instead of EF-owned columns:</para>
/// <list type="bullet">
/// <item>Rule kinds are user-extensible — Phase 9+ may add new shapes without
/// a schema migration. JSON keeps the column shape stable.</item>
/// <item>FluentValidation validates the deserialized shape at the handler boundary
/// (per plan §0.3.3) so an invalid payload fails before it lands.</item>
/// </list>
/// <para>The <c>processed_inbound_events</c> consumer-side idempotency table
/// keys off this aggregate to re-evaluate a rule only when its menu-item
/// set changes.</para>
/// </remarks>
public class DiscountRule : AuditableEntity<int>, ITenantEntity
{
    /// <summary>Tenant scope; mirrors <see cref="Coupon.RestaurantId"/>.</summary>
    public Guid RestaurantId { get; set; }

    /// <summary>FK → <see cref="Coupon.Id"/>. One rule per coupon per UK.</summary>
    public int CouponId { get; set; }

    /// <summary>Discriminator driving the <see cref="RuleDataJson"/> shape.</summary>
    public DiscountRuleKind RuleType { get; set; }

    /// <summary>JSON payload. Shape validated at the handler boundary.</summary>
    public string RuleDataJson { get; set; } = "{}";

    /// <summary>Operator-side activation toggle. Distinct from
    /// <c>AuditableEntity.IsActive</c> (which the interpreter sets
    /// when re-evaluation flips eligibility). Shadowed with
    /// <c>new</c> so the admin-facing flag and the audit flag are
    /// independently addressable on the entity.</summary>
    public new bool IsActive { get; set; } = true;

    /// <summary>Used by the evaluator's <c>Coupon.IsActiveNow</c> propagation.</summary>
    public Instant? DeletedAt { get; set; }

    /// <summary>Soft-delete actor. <see cref="DiscountActors.Sweep"/> or
    /// <see cref="DiscountActors.Service"/> for consumer-driven flips.</summary>
    public string? DeletedBy { get; set; }
}
