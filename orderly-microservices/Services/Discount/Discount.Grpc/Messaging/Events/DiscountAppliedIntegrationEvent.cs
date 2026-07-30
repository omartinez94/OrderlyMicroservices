using BuildingBlocks.Messaging.Events;
using NodaTime;

namespace Discount.Grpc.Messaging.Events;

/// <summary>
/// Published by Discount.Grpc on every successful <c>RedeemDiscount</c> (the
/// atomic conditional-UPDATE path). Architecture names this event but no
/// consumer lands in this plan's window; the publish point is wired-but-disabled
/// behind <c>DiscountOptions:EnableDiscountAppliedPublishing=false</c>
/// </summary>
/// <remarks>
/// <para><b>v2 schema:</b> gained <see cref="OrderId"/> +
/// <see cref="AppliedAt"/>. The base-class <see cref="IntegrationEvent.MessageVersion"/>
/// is bumped to <c>2</c> via the publish site (<see cref="RedeemDiscount"/>);
/// the outbox row's <c>SchemaVersion</c> column mirrors the same value per the
/// plan's <c>MessageVersion ↔ SchemaVersion</c> lockstep rule. Catalog's
/// v1 consumer ignores the new fields per MassTransit's proto3 default.</para>
/// <para><b>Field shape:</b> carries enough context for a downstream
/// receipt-notification or analytics consumer to identify the redeemed
/// coupon without re-querying. <see cref="Quantity"/> defaults to 1 today
/// because the gRPC <c>RedeemDiscountRequest</c> count is server-implied
/// (the conditional UPDATE increments by exactly 1).
/// A future multi-quantity redemption lands a v3 schema bump.</para>
/// <para><b>Base-class fields:</b> <see cref="IntegrationEvent.Id"/>,
/// <see cref="IntegrationEvent.OccurredOn"/>,
/// <see cref="IntegrationEvent.MessageVersion"/> are inherited from
/// <see cref="IntegrationEvent"/>. Do NOT redeclare them on the record.</para>
/// </remarks>
public sealed record DiscountAppliedIntegrationEvent(
    int CouponId,
    string CouponCode,
    Guid RestaurantId,
    int Quantity,
    Guid OrderId,
    Instant AppliedAt) : IntegrationEvent
{
    /// <summary>
    /// v2 schema: gained <see cref="OrderId"/> +
    /// <see cref="AppliedAt"/>. Overriding <see cref="IntegrationEvent.MessageVersion"/>
    /// on the type so every publish site automatically stages the outbox
    /// row with <c>SchemaVersion=2</c>; no publish-site bookkeeping needed.
    /// The OutboxPublisher copies <see cref="IntegrationEvent.MessageVersion"/>
    /// to the outbox row's <c>SchemaVersion</c> column per
    /// <c>OutboxPublisher.cs:43-51</c>.
    /// </summary>
    public override int MessageVersion { get; init; } = 2;
}