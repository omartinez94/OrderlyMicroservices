using BuildingBlocks.Messaging.Events;

namespace Discount.Grpc.Messaging.Events;

/// <summary>
/// Published by Discount.Grpc on every successful <c>RedeemRewardCode</c>
/// (the atomic conditional-UPDATE path). Architecture names this event in §3
/// but no consumer lands in this plan's window, so the publish point is
/// wired-but-disabled behind
/// <c>DiscountOptions:EnableRewardRedeemedPublishing=false</c> per plan §6.5
/// + §7 Phase 6.
/// </summary>
/// <remarks>
/// <para><b>Field shape:</b> mirrors <see cref="DiscountAppliedIntegrationEvent"/>
/// for symmetry — the eventual receipt / notification surface reads both
/// events from the same handler. <see cref="Quantity"/> is the per-call
/// redemption count (mirrors the <c>RedeemRewardCodeRequest.Quantity</c>
/// field which may be &gt; 1 for non-<see cref="Models.RewardKind.FreeItem"/>
/// rewards per §0.3.3).</para>
/// <para><b>Base-class fields:</b> inherited from <see cref="IntegrationEvent"/>
/// (Id / OccurredOn / MessageVersion = 1). Do NOT redeclare per plan v1.1 H3.</para>
/// </remarks>
public sealed record RewardRedeemedIntegrationEvent(
    int RewardCodeId,
    string Code,
    Guid RestaurantId,
    Guid OrderId,
    int Quantity) : IntegrationEvent;
