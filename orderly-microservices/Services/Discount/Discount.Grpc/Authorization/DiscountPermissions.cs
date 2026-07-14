namespace Discount.Grpc.Authorization;

/// <summary>
/// Single source of truth for the permission strings enforced by Discount.Grpc.
/// Identity's follow-up plan reads the same constants to seed its <c>Permissions</c>
/// table and <c>RolePermissions</c> mappings.
/// All identifiers use the hyphenated <c>&lt;entity&gt;:&lt;verb&gt;</c> convention so
/// the three families (coupon, reward-code, discount-rule) read consistently.
/// </summary>
public static class DiscountPermissions
{
    // Coupon CRUD + redemption (Consumer work reuses these).
    public const string CouponRead = "coupon:read";
    public const string CouponCreate = "coupon:create";
    public const string CouponEdit = "coupon:edit";
    public const string CouponDelete = "coupon:delete";
    public const string CouponRedeem = "coupon:redeem";

    // Reward code CRUD + redemption (Constants locked here so
    // Identity can seed role→permission mappings in parallel).
    public const string RewardCodeRead = "reward-code:read";
    public const string RewardCodeCreate = "reward-code:create";
    public const string RewardCodeEdit = "reward-code:edit";
    public const string RewardCodeDelete = "reward-code:delete";
    public const string RewardCodeRedeem = "reward-code:redeem";

    // Discount rule read/edit (Evaluation is its own verb
    // belongs to the apply-surface; tracked as a future "coupon:apply" reservation
    // per plan §Phase 8 doc-update scope).
    public const string DiscountRuleRead = "discount-rule:read";
    public const string DiscountRuleEdit = "discount-rule:edit";

    /// <summary>
    /// All permission strings, in declaration order. Looped by
    /// <see cref="AuthorizationPolicies.AddDiscountPolicies"/> to register each
    /// as a claim-gated policy. Locking this list is the plan's single source
    /// of truth for the gRPC method↔permission mapping.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        CouponRead, CouponCreate, CouponEdit, CouponDelete, CouponRedeem,
        RewardCodeRead, RewardCodeCreate, RewardCodeEdit, RewardCodeDelete, RewardCodeRedeem,
        DiscountRuleRead, DiscountRuleEdit,
    ];
}
