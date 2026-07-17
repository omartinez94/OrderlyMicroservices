namespace BuildingBlocks.Discounts;

/// <summary>
/// One discount applied to a basket / order total. Carries the kind + amount +
/// the originating coupon id + code so the breakdown can round-trip back to the
/// coupon row. <see cref="IsActive"/> lets the caller include a row that didn't
/// actually reduce the total (the helper records an entry with
/// <c>AppliedAmount = 0m</c> instead of filtering — see
/// <see cref="BuildingBlocks.Discounts.ApplyDiscountsHelper.Apply"/> for the contract).
/// </summary>
/// <param name="Type">Closed <see cref="DiscountType"/> discriminator.</param>
/// <param name="Amount">Kind-specific: percentage in (0, 100] OR
/// currency &gt; 0 for <see cref="DiscountType.FixedAmount"/>.</param>
/// <param name="CouponId">Originating coupon's PK.</param>
/// <param name="Code">Originating coupon's code (carried verbatim on the
/// breakdown so admins can audit which code produced which reduction).</param>
/// <param name="IsActive">Whether this coupon is currently active. The helper
/// does NOT filter inactive rows — it records an entry with
/// <see cref="AppliedDiscountBreakdown.AppliedAmount"/> = 0m for
/// inactive rows so the breakdown stays faithful to the input.</param>
public sealed record AppliedDiscount(
    DiscountType Type,
    decimal Amount,
    int CouponId,
    string Code,
    bool IsActive);
