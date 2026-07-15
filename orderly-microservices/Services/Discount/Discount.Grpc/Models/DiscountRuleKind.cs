namespace Discount.Grpc.Models;

/// <summary>
/// Discriminator for <see cref="DiscountRule.RuleDataJson"/>. Drives the
/// evaluator's branching — each kind has a distinct JSON payload shape
/// enforced by FluentValidation at the handler boundary (plan §0.3.3).
/// Renamed from <c>DiscountRuleType</c> to disambiguate from the
/// proto-generated <c>Discount.Grpc.DiscountRuleType</c> enum that
/// lives in the same assembly under the same C# namespace.
/// </summary>
public enum DiscountRuleKind
{
    /// <summary>Coupon applies only when the order subtotal meets a floor.
    /// <c>RuleDataJson = { MinOrderAmount: decimal }</c>.</summary>
    MinOrderAmount = 0,

    /// <summary>Coupon applies only when the order includes specific menu items.
    /// <c>RuleDataJson = { RequiredMenuItemIds: Guid[] }</c>.</summary>
    RequiredMenuItems = 1,

    /// <summary>Coupon applies only inside a time window.
    /// <c>RuleDataJson = { StartTime: time, EndTime: time, DayOfWeekMask: int }</c>.</summary>
    TimeWindow = 2,

    /// <summary>Buy-one-get-one: <c>RuleDataJson = { BuyQuantity: int, GetQuantity: int }</c>.</summary>
    Bogo = 3,
}
