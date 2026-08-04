namespace BuildingBlocks.Discounts;

/// <summary>
/// Pure-static stacking math for a basket / order total. Lives in BuildingBlocks
/// so the Basket preview-time computation and the Ordering finalize-time
/// computation produce the same result from the same <see cref="AppliedDiscount"/>
/// input (per plan §7 Phase 8).
/// </summary>
/// <remarks>
/// <para><b>Stacking semantics:</b> discounts are applied sequentially
/// against the running subtotal. <see cref="DiscountType.Percentage"/>
/// scales the running subtotal by the percentage; <see cref="DiscountType.FixedAmount"/>
/// subtracts <c>Amount</c> verbatim. Per-line rounding is
/// <see cref="MidpointRounding.ToEven"/> (banker's rounding) so the
/// basket-side computation and the Ordering-side computation don't drift.</para>
/// <para><b>Floor-at-zero:</b> the running subtotal never goes below 0. A
/// second Percentage(100) coupon at subtotal $10 + first $10 reduction
/// clamps at $0 with the second coupon's
/// <see cref="AppliedDiscountBreakdown.AppliedAmount"/> = 0m (not
/// <c>-Amount</c>). Per plan §8.2 bullet "Stack of two Percentage(100)
/// coupons against $10 → first reductions bring to $0; second coupon is
/// a no-op (no negative application)."</para>
/// <para><b>Inactive coupons:</b> the helper does NOT filter inactive rows
/// from <c>applied</c>; the caller decides whether to include them.
/// Inactive rows appear in <see cref="ApplyDiscountsResult.Breakdown"/>
/// with <see cref="AppliedDiscountBreakdown.AppliedAmount"/> = 0m.</para>
/// </remarks>
public static class ApplyDiscountsHelper
{
    /// <summary>
    /// Applies all <paramref name="applied"/> discounts sequentially against
    /// <paramref name="subtotal"/>. The result captures the original subtotal,
    /// the sum of all <see cref="AppliedDiscountBreakdown.AppliedAmount"/>
    /// values, the post-clamp effective subtotal, and the per-row breakdown.
    /// </summary>
    public static ApplyDiscountsResult Apply(
        decimal subtotal,
        IReadOnlyList<AppliedDiscount> applied)
    {
        ArgumentNullException.ThrowIfNull(applied);

        var originalSubtotal = subtotal;
        var running = subtotal;
        var breakdown = new List<AppliedDiscountBreakdown>(applied.Count);
        decimal totalReduction = 0m;

        foreach (var d in applied)
        {
            // Inactive coupons: record zero applied, do not reduce the running total.
            if (!d.IsActive)
            {
                breakdown.Add(new AppliedDiscountBreakdown(
                    CouponId: d.CouponId,
                    Code: d.Code,
                    Type: d.Type,
                    RequestedAmount: d.Amount,
                    AppliedAmount: 0m));
                continue;
            }

            var requested = d.Amount;
            decimal rawReduction = d.Type switch
            {
                // Per-line rounding (banker's rounding) applies to the
                // Percentage computation — the resulting reduction may be
                // a non-unit fraction (10% of $1.99 = $0.199 → $0.20).
                DiscountType.Percentage => Math.Round(running * (requested / 100m), MidpointRounding.ToEven),
                // FixedAmount is verbatim: the caller already supplied a
                // whole-unit currency value; rounding here would silently
                // change $33.34 to $33.00 (banker's rounds .34 down at zero
                // decimals) and break the contract tests.
                DiscountType.FixedAmount => requested,
                _ => throw new ArgumentOutOfRangeException(nameof(d), d.Type,
                    $"Unsupported {nameof(DiscountType)} value: {d.Type}."),
            };

            // Floor at zero: never let running go below 0; the candidate's
            // applied contribution is the lesser of (rawReduction, running).
            var appliedReduction = rawReduction > running ? running : rawReduction;
            running -= appliedReduction;
            if (running < 0m) running = 0m;
            totalReduction += appliedReduction;

            breakdown.Add(new AppliedDiscountBreakdown(
                CouponId: d.CouponId,
                Code: d.Code,
                Type: d.Type,
                RequestedAmount: requested,
                AppliedAmount: appliedReduction));
        }

        // Effective subtotal = clamp(raw running, 0, +∞). The running is already
        // floored per-iteration, but the caller may have passed a negative
        // subtotal which we round back to zero here.
        var effective = running < 0m ? 0m : running;

        return new ApplyDiscountsResult(
            OriginalSubtotal: originalSubtotal,
            TotalReduction: totalReduction,
            EffectiveSubtotal: effective,
            Breakdown: breakdown);
    }

    /// <summary>
    /// Single-coupon helper used by tests + the Phase 8 stub consumer's
    /// per-rule evaluation path. Convenience wrapper around
    /// <see cref="Apply(IReadOnlyList{AppliedDiscount}, decimal)"/>
    /// so the call site stays one-liner-pretty.
    /// </summary>
    public static ApplyDiscountsResult ApplyOne(
        decimal subtotal,
        DiscountType type,
        decimal amount,
        int couponId = 0,
        string code = "")
    {
        return Apply(
            subtotal,
            [new AppliedDiscount(type, amount, couponId, code, IsActive: true)]);
    }
}
