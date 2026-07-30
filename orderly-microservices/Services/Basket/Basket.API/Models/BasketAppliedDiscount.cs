namespace Basket.API.Models;

/// <summary>
/// per-coupon row embedded in the
/// <see cref="Basket.AppliedDiscountBreakdown"/> child list. Carries
/// the full output of <see cref="BuildingBlocks.Discounts.ApplyDiscountsHelper.Apply"/>
/// (CouponId, Code, DiscountType, RequestedAmount, AppliedAmount,
/// AppliedAt) so the cart UI can render a customer-visible
/// "X% off applied" line per coupon and admins can audit which
/// coupons were active at upsert time.
/// </summary>
/// <param name="CouponId">Originating coupon's PK.</param>
/// <param name="Code">Originating coupon's code (carried verbatim on the
/// breakdown so admins can audit which code produced which reduction).</param>
/// <param name="DiscountType">Closed <see cref="BuildingBlocks.Discounts.DiscountType"/>
/// discriminator (persisted as int — matches the SQLite-stored
/// column on Coupon).</param>
/// <param name="RequestedAmount">Amount the coupon asked for — pre-clamp.</param>
/// <param name="AppliedAmount">Amount actually deducted — post-clamp
/// (per the floor-at-zero + MidpointRounding.ToEven rounding policy).</param>
/// <param name="AppliedAt">Stamp from <see cref="TimeProvider"/>
/// at upsert time.</param>
public sealed record BasketAppliedDiscount(
    int CouponId,
    string Code,
    int DiscountType,
    decimal RequestedAmount,
    decimal AppliedAmount,
    Instant AppliedAt);