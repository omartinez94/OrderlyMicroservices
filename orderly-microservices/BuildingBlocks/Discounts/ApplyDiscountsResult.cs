namespace BuildingBlocks.Discounts;

/// <summary>
/// Output of <see cref="ApplyDiscountsHelper.Apply"/>: the original
/// subtotal, the total reduction, the post-clamp effective subtotal, and a
/// per-discount breakdown so the caller can persist what was applied +
/// how much.
/// </summary>
public sealed record ApplyDiscountsResult(
    decimal OriginalSubtotal,
    decimal TotalReduction,
    decimal EffectiveSubtotal,
    IReadOnlyList<AppliedDiscountBreakdown> Breakdown);
