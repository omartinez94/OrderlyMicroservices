using BuildingBlocks.Discounts;
using FluentAssertions;

// The proto-generated `Discount.Grpc.DiscountType` enum shadows the
// namespace-unqualified `BuildingBlocks.Discounts.DiscountType` in this
// file (which references both). Alias the BuildingBlocks one locally so
// references below read as the closed-discriminator enum (not the
// wire-shape proto type).
using DbDiscountType = BuildingBlocks.Discounts.DiscountType;

namespace Discount.Grpc.Tests.Unit;

/// <summary>
/// Locks the §8.2 behaviour contract of
/// <see cref="ApplyDiscountsHelper"/>. Lives in
/// <c>Discount.Grpc.Tests/Unit/</c> rather than a new
/// <c>BuildingBlocks.Tests/Discounts/</c> project because Discount is
/// the only adopter for the §8 ship and Discount.Tests already
/// references BuildingBlocks transitively. Future plans that
/// adopt the helper (a Basket side that wants the same composition)
/// can either lift these tests to a shared project or duplicate
/// the small set — the contract is locked and stable.
/// </summary>
public sealed class ApplyDiscountsHelperTests
{
    // ── §8.2 behaviour bullets ────────────────────────────────────────

    [Fact]
    public void EmptyAppliedList_LeavesSubtotalUntouched()
    {
        var result = ApplyDiscountsHelper.Apply(100m, []);

        result.OriginalSubtotal.Should().Be(100m);
        result.TotalReduction.Should().Be(0m);
        result.EffectiveSubtotal.Should().Be(100m);
        result.Breakdown.Should().BeEmpty();
    }

    [Fact]
    public void PercentageTen_AgainstHundred_ReducesTen()
    {
        var result = ApplyDiscountsHelper.ApplyOne(
            subtotal: 100m,
            type: DbDiscountType.Percentage,
            amount: 10m,
            couponId: 1,
            code: "PCT10");

        result.TotalReduction.Should().Be(10m);
        result.EffectiveSubtotal.Should().Be(90m);
        result.Breakdown.Should().ContainSingle(b =>
            b.CouponId == 1 &&
            b.Code == "PCT10" &&
            b.AppliedAmount == 10m);
    }

    [Fact]
    public void FixedAmountTen_AgainstHundred_ReducesTen()
    {
        var result = ApplyDiscountsHelper.ApplyOne(
            subtotal: 100m,
            type: DbDiscountType.FixedAmount,
            amount: 10m,
            couponId: 2,
            code: "FIXED10");

        result.TotalReduction.Should().Be(10m);
        result.EffectiveSubtotal.Should().Be(90m);
    }

    [Fact]
    public void StackPercentage10PlusFixed5_ReducesFifteen()
    {
        var applied = new[]
        {
            new AppliedDiscount(DbDiscountType.Percentage, 10m, 1, "PCT10", true),
            new AppliedDiscount(DbDiscountType.FixedAmount,  5m, 2, "FIXED5", true),
        };

        var result = ApplyDiscountsHelper.Apply(100m, applied);

        result.TotalReduction.Should().Be(15m, "10 + 5 = 15");
        result.EffectiveSubtotal.Should().Be(85m);
        result.Breakdown.Should().HaveCount(2);
    }

    /// <summary>
    /// The §8.2 floor-at-zero edge case: subtotal $10 + two Percentage(100)
    /// coupons must clamp at $0, not -$110.
    /// </summary>
    [Fact]
    public void TwoPercentageHundreds_AgainstTen_FloorAtZero_NoNegative()
    {
        var applied = new[]
        {
            new AppliedDiscount(DbDiscountType.Percentage, 100m, 1, "PCT100A", true),
            new AppliedDiscount(DbDiscountType.Percentage, 100m, 2, "PCT100B", true),
        };

        var result = ApplyDiscountsHelper.Apply(10m, applied);

        result.EffectiveSubtotal.Should().Be(0m, "second Percentage(100) clamps at zero");
        result.TotalReduction.Should().Be(10m,
            "the second coupon is a no-op (AppliedAmount=0); only the first $10 reduction counts");
        result.Breakdown.Should().HaveCount(2);
        result.Breakdown[0].AppliedAmount.Should().Be(10m,
            "first coupon absorbs the full subtotal");
        result.Breakdown[1].AppliedAmount.Should().Be(0m,
            "second coupon is the no-op clamp edge case");
    }

    [Fact]
    public void InactiveCoupon_RecordedInBreakdownWithZeroApplied()
    {
        var applied = new[]
        {
            new AppliedDiscount(DbDiscountType.Percentage, 10m, 1, "PCT10-INACTIVE", IsActive: false),
        };

        var result = ApplyDiscountsHelper.Apply(100m, applied);

        result.TotalReduction.Should().Be(0m, "inactive coupons do not reduce the running total");
        result.EffectiveSubtotal.Should().Be(100m);
        result.Breakdown.Should().ContainSingle(b =>
            b.CouponId == 1 &&
            b.AppliedAmount == 0m &&
            b.RequestedAmount == 10m);
    }

    [Fact]
    public void MixedActiveAndInactive_InactiveZeros_ActiveReduces()
    {
        var applied = new[]
        {
            new AppliedDiscount(DbDiscountType.Percentage, 10m, 1, "PCT10-ACTIVE", true),
            new AppliedDiscount(DbDiscountType.Percentage, 50m, 2, "PCT50-INACTIVE", false),
        };

        var result = ApplyDiscountsHelper.Apply(100m, applied);

        result.TotalReduction.Should().Be(10m, "only the active coupon reduces");
        result.EffectiveSubtotal.Should().Be(90m);
        result.Breakdown.Should().HaveCount(2);
    }

    /// <summary>
    /// Stacking math rounding edge case: subtracting $33.33, $33.33, $33.34
    /// from $100 with bankers' rounding should leave $0.01 (the math): the
    /// running subtotal at each step is exact decimal arithmetic, no floats.
    /// </summary>
    [Fact]
    public void StackingRoundingEdgeCase_FixedAmount33_33_33_34_AgainstHundred_SumsExactly()
    {
        var applied = new[]
        {
            new AppliedDiscount(DbDiscountType.FixedAmount, 33.33m, 1, "FIX33A", true),
            new AppliedDiscount(DbDiscountType.FixedAmount, 33.33m, 2, "FIX33B", true),
            new AppliedDiscount(DbDiscountType.FixedAmount, 33.34m, 3, "FIX33C", true),
        };

        var result = ApplyDiscountsHelper.Apply(100m, applied);

        result.EffectiveSubtotal.Should().Be(0m,
            "33.33 + 33.33 + 33.34 = 100.00 exactly; running hits 0 precisely");
        result.TotalReduction.Should().Be(100m);
    }

    [Fact]
    public void NegativeSubtotal_FloorsAtZero()
    {
        var result = ApplyDiscountsHelper.Apply(-5m, []);

        result.OriginalSubtotal.Should().Be(-5m);
        result.EffectiveSubtotal.Should().Be(0m,
            "the final clamp floors the effective subtotal at zero even when the caller passes a negative starting point");
    }

    [Fact]
    public void BreakdownOrder_MatchesAppliedInputOrder()
    {
        var applied = new[]
        {
            new AppliedDiscount(DbDiscountType.Percentage, 10m, 100, "PCT", true),
            new AppliedDiscount(DbDiscountType.FixedAmount,   5m, 200, "FIX", true),
            new AppliedDiscount(DbDiscountType.Percentage, 50m, 300, "PCT-DEFER", false),
        };

        var result = ApplyDiscountsHelper.Apply(100m, applied);

        result.Breakdown[0].CouponId.Should().Be(100);
        result.Breakdown[1].CouponId.Should().Be(200);
        result.Breakdown[2].CouponId.Should().Be(300);
    }

    [Fact]
    public void PercentageGreaterThanHundred_BelowCap_StillReducesEverything()
    {
        // Validator caps Percentage at (0, 100] per §0.3.3, but the
        // helper itself is forgiving — a future v2 might raise the cap.
        // This test pins today's behaviour: Percentage > 100 reduces
        // everything that remains, plus clamps.
        var result = ApplyDiscountsHelper.ApplyOne(
            subtotal: 50m,
            type: DbDiscountType.Percentage,
            amount: 200m);

        result.TotalReduction.Should().Be(50m, "200% of 50 = 100, but capped at the running subtotal");
        result.EffectiveSubtotal.Should().Be(0m);
    }
}
