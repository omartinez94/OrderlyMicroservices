using BuildingBlocks.Discounts;

namespace BuildingBlocks.Tests.Discounts;

/// <summary>
/// Pins the Phase 8 contract for <see cref="ApplyDiscountsHelper"/>.
/// The helper is the single source of truth for stacking math shared
/// between Basket (preview-time) and Ordering (finalize-time);
/// behavior drift here breaks both surfaces simultaneously.
/// </summary>
public class ApplyDiscountsHelperTests
{
    [Fact]
    public void EmptyApplied_ReturnsIdentityResult()
    {
        var result = ApplyDiscountsHelper.Apply(subtotal: 100m, applied: []);

        result.OriginalSubtotal.Should().Be(100m);
        result.TotalReduction.Should().Be(0m);
        result.EffectiveSubtotal.Should().Be(100m);
        result.Breakdown.Should().BeEmpty();
    }

    [Fact]
    public void OnePercentage_TenPercent_ReducesByTen()
    {
        var result = ApplyDiscountsHelper.Apply(100m, [
            new AppliedDiscount(DiscountType.Percentage, 10m, CouponId: 1, Code: "P10", IsActive: true),
        ]);

        result.TotalReduction.Should().Be(10m);
        result.EffectiveSubtotal.Should().Be(90m);
        result.Breakdown.Should().HaveCount(1);
        result.Breakdown[0].AppliedAmount.Should().Be(10m);
    }

    [Fact]
    public void OneFixedAmount_FiveDollars_ReducesByFive()
    {
        var result = ApplyDiscountsHelper.Apply(100m, [
            new AppliedDiscount(DiscountType.FixedAmount, 5m, CouponId: 1, Code: "F5", IsActive: true),
        ]);

        result.TotalReduction.Should().Be(5m);
        result.EffectiveSubtotal.Should().Be(95m);
    }

    [Fact]
    public void StackPercentageTenPlusFixedFive_ReducesByFifteen()
    {
        var result = ApplyDiscountsHelper.Apply(100m, [
            new AppliedDiscount(DiscountType.Percentage, 10m, CouponId: 1, Code: "P10", IsActive: true),
            new AppliedDiscount(DiscountType.FixedAmount, 5m, CouponId: 2, Code: "F5", IsActive: true),
        ]);

        result.TotalReduction.Should().Be(15m);
        result.EffectiveSubtotal.Should().Be(85m);
    }

    [Fact]
    public void StackTwoPercentageHundred_ClampsAtZero()
    {
        // First P(100) brings $10 → $0; second P(100) is a no-op
        // (running total never goes negative). Breakdown[1] records
        // AppliedAmount = 0m.
        var result = ApplyDiscountsHelper.Apply(10m, [
            new AppliedDiscount(DiscountType.Percentage, 100m, CouponId: 1, Code: "P100A", IsActive: true),
            new AppliedDiscount(DiscountType.Percentage, 100m, CouponId: 2, Code: "P100B", IsActive: true),
        ]);

        result.EffectiveSubtotal.Should().Be(0m);
        result.TotalReduction.Should().Be(10m);
        result.Breakdown[0].AppliedAmount.Should().Be(10m);
        result.Breakdown[1].AppliedAmount.Should().Be(0m);
    }

    [Fact]
    public void InactiveCoupon_RecordsZeroApplied_NoReduction()
    {
        var result = ApplyDiscountsHelper.Apply(100m, [
            new AppliedDiscount(DiscountType.Percentage, 10m, CouponId: 1, Code: "P10INACTIVE", IsActive: false),
        ]);

        result.TotalReduction.Should().Be(0m);
        result.EffectiveSubtotal.Should().Be(100m);
        result.Breakdown.Should().HaveCount(1);
        result.Breakdown[0].AppliedAmount.Should().Be(0m);
    }

    [Fact]
    public void BankersRounding_HalfEvenEdge_RoundsToEven()
    {
        // 0.005 + 0.005 = 0.01 with banker's rounding. The
        // MidpointRounding.ToEven policy means 1.005 rounds to 1.00
        // (the even neighbor) not 1.01. Verifies the helper pins
        // banker's rounding — a future refactor that switches to
        // AwayFromZero would silently change every 0.005-edge
        // coupon calculation.
        var result = ApplyDiscountsHelper.Apply(1m, [
            new AppliedDiscount(DiscountType.FixedAmount, 0.005m, CouponId: 1, Code: "R1", IsActive: true),
            new AppliedDiscount(DiscountType.FixedAmount, 0.005m, CouponId: 2, Code: "R2", IsActive: true),
        ]);

        result.TotalReduction.Should().Be(0.01m);
        result.EffectiveSubtotal.Should().Be(0.99m);
    }

    [Fact]
    public void PercentageOnLargeSubtotal_PerLineRounding()
    {
        // 10% of $19.99 = $1.999; banker's rounds to $2.00 (the even
        // neighbor at 1.99 → 2.00 is even, 1.999 → 1.99 is also a
        // midpoint — banker's picks the even digit, which is 2.00).
        var result = ApplyDiscountsHelper.Apply(19.99m, [
            new AppliedDiscount(DiscountType.Percentage, 10m, CouponId: 1, Code: "P10", IsActive: true),
        ]);

        result.TotalReduction.Should().Be(2m);
        result.EffectiveSubtotal.Should().Be(17.99m);
    }

    [Fact]
    public void FixedAmountGreaterThanRemaining_ReducesOnlyByRemaining()
    {
        // $50 fixed against $30 subtotal: reduction = $30, not $50.
        // The breakdown records the cap-at-remaining semantic.
        var result = ApplyDiscountsHelper.Apply(30m, [
            new AppliedDiscount(DiscountType.FixedAmount, 50m, CouponId: 1, Code: "F50", IsActive: true),
        ]);

        result.TotalReduction.Should().Be(30m);
        result.EffectiveSubtotal.Should().Be(0m);
        result.Breakdown[0].AppliedAmount.Should().Be(30m);
        result.Breakdown[0].RequestedAmount.Should().Be(50m);
    }

    [Fact]
    public void ApplyOne_SingleCouponHelper_RoundTrip()
    {
        // ApplyOne is a one-line wrapper around Apply; the
        // round-trip is exercised to catch a future regression where
        // someone accidentally forks the single-coupon path.
        var result = ApplyDiscountsHelper.ApplyOne(100m, DiscountType.Percentage, 25m, couponId: 42, code: "P25");

        result.TotalReduction.Should().Be(25m);
        result.EffectiveSubtotal.Should().Be(75m);
        result.Breakdown.Should().HaveCount(1);
        result.Breakdown[0].CouponId.Should().Be(42);
        result.Breakdown[0].Code.Should().Be("P25");
    }
}