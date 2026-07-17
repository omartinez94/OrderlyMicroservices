using BuildingBlocks.Messaging.Events;

namespace Discount.Grpc.Messaging.Events;

/// <summary>
/// Published by Discount.Grpc on every successful <c>RedeemDiscount</c> (the
/// atomic conditional-UPDATE path). Architecture names this event in §3 but no
/// consumer lands in this plan's window; the publish point is wired-but-disabled
/// behind <c>DiscountOptions:EnableDiscountAppliedPublishing=false</c> per
/// plan §6.5 + §7 Phase 6.
/// </summary>
/// <remarks>
/// <para><b>Field shape:</b> carries enough context for a downstream
/// receipt-notification or analytics consumer to identify the redeemed
/// coupon without re-querying. <see cref="Quantity"/> defaults to 1 today
/// because the gRPC <c>RedeemDiscountRequest</c> count is server-implied
/// (the conditional UPDATE increments by exactly 1 — per plan v1.1 L11).
/// A future multi-quantity redemption lands a v2 schema bump.</para>
/// <para><b>OrderId is intentionally absent from this event.</b> Plan
/// §0.3.3 lists <c>RedeemDiscountCommand.OrderId</c> as a required field,
/// but the shipped <c>RedeemDiscountRequest</c> proto in
/// <c>Protos/discount.proto</c> doesn't carry an <c>order_id</c>. Adding
/// OrderId requires a proto extension + Basket's
/// <c>DiscountProtoService.DiscountProtoServiceClient</c> in lockstep;
/// tracked for the §0.3.3 reconciliation pass (Phase 7 / Phase 8
/// cleanup), out of Phase 6's scope.</para>
/// <para><b>Base-class fields:</b> <see cref="IntegrationEvent.Id"/>,
/// <see cref="IntegrationEvent.OccurredOn"/>,
/// <see cref="IntegrationEvent.MessageVersion"/> = 1 are inherited from
/// <see cref="IntegrationEvent"/>. Do NOT redeclare them on the record
/// (plan v1.1 H3).</para>
/// </remarks>
public sealed record DiscountAppliedIntegrationEvent(
    int CouponId,
    string CouponCode,
    Guid RestaurantId,
    int Quantity) : IntegrationEvent;
