using BuildingBlocks.Messaging.Events;

namespace Discount.Grpc.Messaging.Events;

/// <summary>
/// Published by Discount.Grpc on every <c>RewardCodeService.CreateRewardCode</c>
/// (and via the wired-but-disabled <c>FeedbackSubmittedConsumer</c> Phase 5
/// path). Architecture names this event in §3; no consumer lands in this
/// plan's window, so the publish point is gated behind
/// <c>DiscountOptions:EnableRewardGeneratedPublishing=false</c> per plan
/// §6.5 + §7 Phase 6.
/// </summary>
/// <remarks>
/// <para><b>Why publish even when no consumer is wired:</b>
/// <see cref="RewardGeneratedIntegrationEvent"/> is the canonical
/// customer-side notification ("here's your reward code") that future
/// marketing / loyalty flows will consume. The publish-point ships
/// disabled so the wire contract lands in lockstep with the entity
/// creation; flipping <c>EnableRewardGeneratedPublishing=true</c> is a
/// config-only change and starts emitting rows on the next boot.</para>
/// <para><b>Field shape:</b> the row is a customer-facing reward code so
/// <see cref="Code"/> is exposed verbatim — the publisher carries it on the
/// wire (the consumer is expected to forward it to the customer). Tenant
/// scope via <see cref="RestaurantId"/>.</para>
/// <para><b>Base-class fields:</b> inherited from <see cref="IntegrationEvent"/>
/// (Id / OccurredOn / MessageVersion = 1). Do NOT redeclare per plan v1.1 H3.</para>
/// </remarks>
public sealed record RewardGeneratedIntegrationEvent(
    int RewardCodeId,
    string Code,
    Guid RestaurantId,
    string Kind,
    decimal Value,
    Guid? OrderId) : IntegrationEvent;
