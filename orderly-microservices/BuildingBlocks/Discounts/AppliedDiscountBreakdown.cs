namespace BuildingBlocks.Discounts;

/// <summary>
/// Per-discount row of the <see cref="ApplyDiscountsResult.Breakdown"/>.
/// Records the <c>RequestedAmount</c> the caller asked for and the
/// <c>AppliedAmount</c> the helper actually deducted (the two diverge on the
/// floor-at-zero clamp edge case where a second Percentage(100) coupon
/// would push the running total below zero).
/// </summary>
/// <param name="CouponId">Originating coupon's PK.</param>
/// <param name="Code">Originating coupon's code.</param>
/// <param name="Type"><see cref="DiscountType"/> discriminator.</param>
/// <param name="RequestedAmount">Amount the caller asked for — pre-clamp.</param>
/// <param name="AppliedAmount">Amount actually deducted — post-clamp.</param>
public sealed record AppliedDiscountBreakdown(
    int CouponId,
    string Code,
    DiscountType Type,
    decimal RequestedAmount,
    decimal AppliedAmount);
