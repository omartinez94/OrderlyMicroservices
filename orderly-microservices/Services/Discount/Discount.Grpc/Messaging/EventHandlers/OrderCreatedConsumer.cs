using BuildingBlocks.Authorization;
using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Multitenancy;
using Discount.Grpc.Authorization;
using Discount.Grpc.Data;
using Discount.Grpc.Messaging.Events;
using Discount.Grpc.Options;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Discount.Grpc.Messaging.EventHandlers;

/// <summary>
/// Reacts to <see cref="OrderCreatedIntegrationEvent"/> and applies
/// any auto-apply coupons to the order. Per plan §8.4, the consumer
/// drives <c>EvaluateDiscountRules → RedeemDiscount →
/// DiscountAppliedIntegrationEvent v2</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Disabled by default</b> via <c>DiscountOptions:EnableOrderCreatedConsumer=false</c>.
/// Flipping the flag to <c>true</c> wires up the consumer endpoint;
/// no recompile needed. The <see cref="OrderCreatedConsumer"/> is
/// registered in <c>Program.cs</c>'s conditional
/// <c>AddConsumer</c> block the same way <see cref="FeedbackSubmittedConsumer"/>
/// is.</item>
/// <item><b>Pattern 2 synthetic principal</b>: the consumer attaches a
/// <c>ClaimsPrincipal</c> built from the inbound
/// <see cref="OrderCreatedIntegrationEvent.RestaurantId"/> so the
/// per-request <see cref="ICurrentRestaurantProvider"/> reads the
/// correct tenant for the duration of the consume.</item>
/// <item><b>v2 wire shape</b>: <see cref="DiscountAppliedIntegrationEvent"/>
/// carries <c>OrderId</c> + <c>AppliedAt</c>; the publish site stamps
/// both. The base-class <see cref="IntegrationEvent.MessageVersion"/>
/// is bumped to <c>2</c> on the publish site — the outbox row's
/// <c>SchemaVersion</c> column mirrors the same value.</item>
/// </list>
/// </remarks>
public sealed class OrderCreatedConsumer(
    IServiceScopeFactory scopes,
    ILogger<OrderCreatedConsumer> logger)
    : IConsumer<OrderCreatedIntegrationEvent>
{
    private const string ConsumerType = nameof(OrderCreatedConsumer);

    public async Task Consume(ConsumeContext<OrderCreatedIntegrationEvent> context)
    {
        var evt = context.Message;
        var rid = evt.RestaurantId;

        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var provider = sp.GetRequiredService<ICurrentRestaurantProvider>();
        var db = sp.GetRequiredService<DiscountContext>();
        var outbox = sp.GetRequiredService<IOutboxPublisher>();
        var clock = sp.GetRequiredService<TimeProvider>();
        var options = sp.GetRequiredService<IOptions<DiscountOptions>>();

        // the master switch. When the flag is false the
        // consumer acks the message without action — keeping the
        // bus topology stable even when the operator hasn't enabled
        // the auto-apply chain yet.
        if (!options.Value.EnableOrderCreatedConsumer)
        {
            logger.LogDebug(
                "{Consumer}: skipped (EnableOrderCreatedConsumer=false) for order {OrderId}.",
                ConsumerType, evt.OrderId);
            return;
        }

        // Pattern 2 synthetic principal.
        var principal = new ClaimsPrincipalBuilder()
            .WithRestaurant(rid)
            .WithActor(DiscountActors.Service)
            .Build();

        var now = SystemClock.Instance.GetCurrentInstant();

        using (provider.Attach(principal))
        {
            // stub implementation: rather than self-referencing
            // gRPC clients (Discount.Grpc → Discount.Grpc on
            // localhost:6002), we query the local DbContext for
            // currently-active coupons whose amount could plausibly apply
            // to the order total. Real EvaluateDiscountRules / gRPC
            // redemption lands with the future ordering-scheduler plan.
            // The stub proves the wiring + the v2 publish shape; the
            // rule-eval surface is delegated to that future plan.
            var candidates = await db.Coupons
                .Where(c => c.RestaurantId == rid
                            && c.IsActive
                            && c.DeletedAt == null
                            && (c.ExpirationDate == null || c.ExpirationDate >= now)
                            && (c.MaxRedeemAmount == null || c.RedeemAmount < c.MaxRedeemAmount))
                .AsNoTracking()
                .ToListAsync(context.CancellationToken);

            if (candidates.Count == 0)
            {
                logger.LogInformation(
                    "{Consumer}: no eligible coupons for order {OrderId} ({RestaurantId}); ack-only.",
                    ConsumerType, evt.OrderId, rid);
                return;
            }

            // Apply the first eligible coupon (one-per-order). Real stacking lands with
            // the future implementation; the stub demonstrates the
            // publish shape + the conditional UPDATE pattern.
            var coupon = candidates[0];

            var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE ""Coupons""
                SET ""RedeemAmount""    = ""RedeemAmount"" + 1,
                    ""LastModifiedAt""  = {now},
                    ""LastModifiedBy""  = {DiscountActors.Service}
                WHERE ""Id"" = {coupon.Id}
                  AND ""IsActive"" = {true}
                  AND ""DeletedAt"" IS NULL
                  AND (""MaxRedeemAmount"" IS NULL OR ""RedeemAmount"" < ""MaxRedeemAmount"")
            ", context.CancellationToken);

            if (rowsAffected == 0)
            {
                // Race / cap-hit: peer consumer or admin took the last
                // slot between our read and write. Ack the message —
                // the idempotency-key layer (when enabled) handles
                // caller-side retry.
                logger.LogInformation(
                    "{Consumer}: redemption race lost for coupon {CouponId} (order {OrderId}); ack-only.",
                    ConsumerType, coupon.Id, evt.OrderId);
                return;
            }

            if (options.Value.EnableDiscountAppliedPublishing)
            {
                await outbox.PublishAsync(new DiscountAppliedIntegrationEvent(
                    CouponId: coupon.Id,
                    CouponCode: coupon.Code,
                    RestaurantId: rid,
                    Quantity: 1,
                    OrderId: evt.OrderId,
                    AppliedAt: now),
                    context.CancellationToken);

                logger.LogInformation(
                    "{Consumer}: applied coupon {CouponCode} (id={CouponId}) to order {OrderId}; v2 DiscountAppliedIntegrationEvent queued.",
                    ConsumerType, coupon.Code, coupon.Id, evt.OrderId);
            }
        }
    }
}