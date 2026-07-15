using BuildingBlocks.Authorization;
using BuildingBlocks.Messaging.Events.Catalog;
using BuildingBlocks.Multitenancy;
using Discount.Grpc.Authorization;
using Discount.Grpc.Data;
using Discount.Grpc.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Discount.Grpc.Messaging.EventHandlers;

/// <summary>
/// Reacts to <see cref="MenuItemChangedIntegrationEvent"/> by re-evaluating
/// active <see cref="DiscountRule"/>s whose <c>RequiredMenuItemIds</c>
/// contains the affected <c>MenuItemId</c>.
/// consumer-side idempotency via the <c>processed_inbound_events</c>
/// table; Pattern 2 claims via <see cref="ICurrentRestaurantProvider.Attach"/>.
/// </summary>
public sealed class MenuItemChangedConsumer(
    IServiceScopeFactory scopes,
    ILogger<MenuItemChangedConsumer> logger) : IConsumer<MenuItemChangedIntegrationEvent>
{
    private const string ConsumerType = nameof(MenuItemChangedConsumer);

    public async Task Consume(ConsumeContext<MenuItemChangedIntegrationEvent> context)
    {
        var evt = context.Message;

        // Pattern 2: synthetic principal for the tenant scope.
        var principal = new ClaimsPrincipalBuilder()
            .WithRestaurant(evt.RestaurantId)
            .WithActor(DiscountActors.Service)
            .Build();

        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var provider = sp.GetRequiredService<ICurrentRestaurantProvider>();
        var db = sp.GetRequiredService<DiscountContext>();
        var dedupEventId = InboundEventDedup.EventId(evt);

        using (provider.Attach(principal))
        {
            // Idempotency gate first — a redelivered event must not
            // re-evaluate the rule set.
            var alreadyProcessed = await InboundEventDedup.TryRecordAsync(
                db, dedupEventId, ConsumerType, context.CancellationToken);

            if (alreadyProcessed)
            {
                logger.LogInformation(
                    "MenuItemChanged {EventId} for {RestaurantId}/{MenuItemId}: already processed, skipping.",
                    dedupEventId, evt.RestaurantId, evt.MenuItemId);
                return;
            }

            // Find affected rules (any active rule that targets this menu item).
            var affectedRuleIds = await db.DiscountRules
                .Where(r => r.IsActive && r.DeletedAt == null
                    && r.RestaurantId == evt.RestaurantId
                    && r.RuleType == DiscountRuleKind.RequiredMenuItems
                    && r.RuleDataJson.Contains($"\"{evt.MenuItemId:N}\""))
                .Select(r => r.CouponId)
                .ToListAsync(context.CancellationToken);

            if (affectedRuleIds.Count == 0)
            {
                logger.LogDebug(
                    "MenuItemChanged {MenuItemId}: no required-menu-items rules reference it.",
                    evt.MenuItemId);
                return;
            }

            // only flips coupons that are now ineligible because
            // the underlying menu item disappeared (ChangeType = Deleted).
            // Updated events leave IsActive alone — re-evaluation is a
            // cron job. Currency-only deactivation lives in
            // RestaurantConfigurationChangedConsumer.
            if (evt.ChangeType != MenuItemChangeType.Deleted)
            {
                logger.LogDebug(
                    "MenuItemChanged {MenuItemId}: ChangeType={ChangeType}; noop.",
                    evt.MenuItemId, evt.ChangeType);
                return;
            }

            // Raw SQL bulk-update — AuditableEntity<T>.IsActive +
            // LastModifiedBy + LastModifiedAt have protected setters that
            // the interceptor stamps; bypassing EF here keeps the
            // deactivation atomic across the affected CouponIds without
            // needing a separate audit hook for the consumer path.
            //
            // The `nowTicks` value mirrors `InstantToLongConverter`'s
            // storage (UnixTimeTicks → INTEGER) so the raw-SQL parameter
            // type is `long`, which the EF Core SQLite provider can map
            // directly. Passing an Instant via ExecuteSqlInterpolatedAsync
            // would surface a parameter-type-mapping error (the
            // converter scopes to column mapping only).
            var nowTicks = SystemClock.Instance.GetCurrentInstant().ToUnixTimeTicks();
            var actor = DiscountActors.Service;
            var idList = string.Join(',', affectedRuleIds);
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE Coupons
                SET IsActive = 0,
                    LastModifiedBy = {actor},
                    LastModifiedAt = {nowTicks}
                WHERE Id IN ({idList})
                  AND RestaurantId = {evt.RestaurantId}
                  AND IsActive = 1
                  AND DeletedAt IS NULL
            ", context.CancellationToken);

            logger.LogInformation(
                "MenuItemChanged {MenuItemId}: deactivated {Count} coupon(s) (Deleted event).",
                evt.MenuItemId, affected);
        }
    }
}
