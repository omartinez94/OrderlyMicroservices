using BuildingBlocks.Entities.Contracts;
using BuildingBlocks.Multitenancy;
using NodaTime;

// The proto-generated `Discount.Grpc.DiscountType` enum shadows the
// namespace-unqualified `DiscountType` from BuildingBlocks.Discounts.
// The BuildingBlocks type is what we map to/from; the proto one is the
// wire-shape. Both are referenced in this file — fully qualify when both
// are in scope.
using DbDiscountType = BuildingBlocks.Discounts.DiscountType;
using ProtoDiscountType = Discount.Grpc.DiscountType;

namespace Discount.Grpc.Models;

public class Coupon : AuditableEntity<int>, ITenantEntity
{
    public Guid RestaurantId { get; set; }
    public required string Code { get; set; }
    public required string Description { get; set; }
    /// <summary>
    /// Closed discriminator controlling the semantic of
    /// <see cref="Amount"/>. Closed enum lives in
    /// <see cref="BuildingBlocks.Discounts"/> so Basket (preview-time
    /// deduction) + Ordering (finalized-order deduction) compile against
    /// the same shape without an RPC roundtrip per plan §8.2.
    /// </summary>
    /// <remarks>
    /// Default = <see cref="DbDiscountType.Percentage"/> matches the
    /// migration's <c>DEFAULT 0</c>; rows existing before the Phase 8
    /// <c>AddDiscountTypeToCoupon</c> migration are re-classified as
    /// Percentage on next read. Pre-migration <c>Amount</c> values that
    /// were interpreted as fixed currency (e.g. <c>$10</c>) will now
    /// reduce by 10 % (e.g. <c>$10 → $9</c>); the audit doc at
    /// <c>docs/discounts/discount-type-seed-audit.md</c> is the operator's
    /// pre-migration review checklist per plan §8.1.
    /// </remarks>
    public DbDiscountType DiscountType { get; set; } = DbDiscountType.Percentage;
    /// <summary>
    /// Pre-Phase-8, the meaning of this field was "currency amount"
    /// or "percentage amount" depending on the discount's nature —
    /// driven entirely by the convention in the admin UI. Phase 8
    /// replaces that ambiguity with the <see cref="DiscountType"/>
    /// column + the <c>BuildingBlocks.Discounts.ApplyDiscountsHelper</c>
    /// contract. See the audit note on
    /// <see cref="DbDiscountType.Percentage"/> for the seed-reclassification risk.
    /// </summary>
    public required decimal Amount { get; set; }
    public int RedeemAmount { get; set; } = 0;
    public int? MaxRedeemAmount { get; set; }
    public Instant? ExpirationDate { get; set; }

    // Soft-delete columns — set by DiscountExpirySweepService when the coupon
    // expires. AuditableEntity.IsActive stays a separate business-flag: an admin
    // can deactivate a coupon (IsActive = false) before its expiration, and the
    // sweep soft-deletes an expired one (DeletedAt != null). Both flag-gates
    // participate in the global query filter — see DiscountContext.OnModelCreating.
    public Instant? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

