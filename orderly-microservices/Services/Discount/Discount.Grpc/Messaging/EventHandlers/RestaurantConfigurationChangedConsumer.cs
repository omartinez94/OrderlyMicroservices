using BuildingBlocks.Authorization;
using BuildingBlocks.Messaging.Events.Catalog;
using BuildingBlocks.Multitenancy;
using Discount.Grpc.Authorization;
using Discount.Grpc.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Discount.Grpc.Messaging.EventHandlers;

/// <summary>
/// Reacts to <see cref="RestaurantConfigurationChangedIntegrationEvent"/>:
/// when the <c>Currency</c> field is in <c>ChangedFields</c>, deactivate
/// the tenant's coupons.
/// </summary>
public sealed class RestaurantConfigurationChangedConsumer(
    IServiceScopeFactory scopes,
    ILogger<RestaurantConfigurationChangedConsumer> logger)
    : IConsumer<RestaurantConfigurationChangedIntegrationEvent>
{
    private const string ConsumerType = nameof(RestaurantConfigurationChangedConsumer);
    private const string CurrencyField = "Currency";

    public async Task Consume(ConsumeContext<RestaurantConfigurationChangedIntegrationEvent> context)
    {
        var evt = context.Message;

        // Currency-only deactivation. Other fields in
        // ChangedFields don't surface new discount eligibility
        // concerns today
        if (!evt.ChangedFields.Contains(CurrencyField))
        {
            logger.LogDebug(
                "RestaurantConfigurationChanged for {RestaurantId}: ChangedFields has no Currency — noop.",
                evt.RestaurantId);
            return;
        }

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
            // Idempotency gate — bus may redeliver.
            if (await InboundEventDedup.TryRecordAsync(
                    db, dedupEventId, ConsumerType, context.CancellationToken))
            {
                logger.LogInformation(
                    "RestaurantConfigurationChanged {EventId} for {RestaurantId}: already processed, skipping.",
                    dedupEventId, evt.RestaurantId);
                return;
            }

            // Raw SQL bulk-update — AuditableEntity<T>.IsActive +
            // LastModifiedBy + LastModifiedAt have protected setters that
            // the interceptor stamps; bypassing EF here keeps the
            // deactivation atomic across the affected tenant without
            // needing a separate audit hook for the consumer path.
            // Mirrors the parameter-type pattern in
            // MenuItemChangedConsumer — `Instant` lacks a raw-SQL
            // parameter mapping, so we pass `long` (UnixTimeTicks) and
            // rely on InstantToLongConverter on the column side.
            var nowTicks = SystemClock.Instance.GetCurrentInstant().ToUnixTimeTicks();
            var actor = DiscountActors.Service;
            var deactivated = await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE Coupons
                SET IsActive = 0,
                    LastModifiedBy = {actor},
                    LastModifiedAt = {nowTicks}
                WHERE RestaurantId = {evt.RestaurantId}
                  AND IsActive = 1
                  AND DeletedAt IS NULL
            ", context.CancellationToken);

            logger.LogInformation(
                "RestaurantConfigurationChanged {RestaurantId}: Currency change deactivated {Count} coupon(s).",
                evt.RestaurantId, deactivated);
        }
    }
}
