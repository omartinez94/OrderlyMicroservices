namespace Discount.Grpc.Models;

/// <summary>
/// Discriminator for <see cref="RewardCode.Value"/>'s overloaded semantics.
/// Each kind has a distinct payload shape enforced by FluentValidation at
/// the handler boundary per plan §0.3.3.
/// </summary>
/// <remarks>
/// <para>Renamed from <c>RewardType</c> to disambiguate from the class name
/// (mirrors the v1.1 S1 / M6 reasoning that landed on the same
/// convention for <c>RewardKind</c>).</para>
/// <para>Intentionally distinct from <see cref="DiscountRuleKind"/>:
/// <c>RewardCode.RewardKind</c> is what a customer gets in exchange for
/// feedback; <c>DiscountRuleKind</c> is the engine-side eligibility
/// predicate that gates when a <c>Coupon</c> is applicable.</para>
/// <para>A future <c>BuildingBlocks.Discounts.DiscountKind</c> consolidation
/// is tracked as a v2 BuildingBlocks contribution (out of this plan's
/// scope per v1.4 Decision A).</para>
/// </remarks>
public enum RewardKind
{
    /// <summary><c>Value</c> is a percentage in <c>(0, 100]</c>.</summary>
    Percentage = 0,

    /// <summary><c>Value</c> is a fixed currency amount (<c>&gt; 0</c>).</summary>
    FixedAmount = 1,

    /// <summary><c>Value</c> must be <c>0</c>; the target menu-item id
    /// lives in <c>Description</c> as <c>free-item:{menuItemId}</c>
    /// (proto v1 stays simple; v2 bumps <c>RewardTargetMenuItemId</c>).</summary>
    FreeItem = 2,

    /// <summary><c>Value</c> is a points count (<c>&gt; 0</c>).</summary>
    Points = 3,
}