using BuildingBlocks.Authorization;
using BuildingBlocks.Multitenancy;
using Microsoft.AspNetCore.SignalR;
using NodaTime;
using Ordering.API.Observability;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Dtos;
using Ordering.Application.Orders.Queries.GetOrdersStats;

namespace Ordering.API.Hubs;

public sealed class SignalROrderNotifier(
    IHubContext<OrdersHub, IOrdersHubClient> hubContext,
    IServiceScopeFactory scopeFactory,
    IClock clock,
    OrderingLiveMetrics metrics,
    ILogger<SignalROrderNotifier> logger)
    : IOrderNotifier
{
    public async Task NotifyUpsertedAsync(
        IReadOnlyList<OrderChangeNotification> notifications,
        CancellationToken cancellationToken = default)
    {
        if (notifications is null || notifications.Count == 0)
        {
            return;
        }

        HashSet<Guid> affectedRestaurants = [];

        foreach (OrderChangeNotification notification in notifications)
        {
            Guid restaurantId = notification.Order.RestaurantId;
            if (restaurantId == Guid.Empty)
            {
                continue;
            }

            affectedRestaurants.Add(restaurantId);

            try
            {
                await hubContext.Clients
                    .Group(OrdersHub.RestaurantGroup(restaurantId))
                    .OrderUpserted(notification.Order, notification.ChangeType, notification.OccurredAt);

                double latencyMs = (clock.GetCurrentInstant() - notification.OccurredAt).TotalMilliseconds;
                metrics.RecordPush(notification.ChangeType, latencyMs);
            }
            catch (Exception ex)
            {
                metrics.RecordPushFailure("order_upserted");
                logger.LogWarning(
                    ex,
                    "Failed to broadcast OrderUpserted for order {OrderId} in restaurant {RestaurantId}.",
                    notification.Order.Id,
                    restaurantId);
            }
        }

        foreach (Guid restaurantId in affectedRestaurants)
        {
            await BroadcastStatsForRestaurantAsync(restaurantId, cancellationToken);
        }
    }

    public async Task NotifyRemovedAsync(
        IReadOnlyList<OrderRemovalNotification> removals,
        CancellationToken cancellationToken = default)
    {
        if (removals is null || removals.Count == 0)
        {
            return;
        }

        HashSet<Guid> affectedRestaurants = [];

        foreach (OrderRemovalNotification removal in removals)
        {
            if (removal.RestaurantId == Guid.Empty)
            {
                continue;
            }

            affectedRestaurants.Add(removal.RestaurantId);

            try
            {
                await hubContext.Clients
                    .Group(OrdersHub.RestaurantGroup(removal.RestaurantId))
                    .OrderRemoved(removal.OrderId);

                double latencyMs = (clock.GetCurrentInstant() - removal.OccurredAt).TotalMilliseconds;
                metrics.RecordPush("Removed", latencyMs);
            }
            catch (Exception ex)
            {
                metrics.RecordPushFailure("order_removed");
                logger.LogWarning(
                    ex,
                    "Failed to broadcast OrderRemoved for order {OrderId} in restaurant {RestaurantId}.",
                    removal.OrderId,
                    removal.RestaurantId);
            }
        }

        foreach (Guid restaurantId in affectedRestaurants)
        {
            await BroadcastStatsForRestaurantAsync(restaurantId, cancellationToken);
        }
    }

    private async Task BroadcastStatsForRestaurantAsync(
        Guid restaurantId,
        CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            ICurrentRestaurantProvider tenantProvider = scope.ServiceProvider.GetRequiredService<ICurrentRestaurantProvider>();
            tenantProvider.Attach(
                new ClaimsPrincipalBuilder()
                    .WithRestaurant(restaurantId)
                    .Build());

            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
            GetOrdersStatsResult fullResult = await sender.Send(
                new GetOrdersStatsQuery(IncludeRevenue: true),
                cancellationToken);

            OrdersStatsDto fullStats = fullResult.Stats;
            OrdersStatsDto redactedStats = fullStats with { RevenueToday = null };

            await hubContext.Clients
                .Group(OrdersHub.StatsGroup(restaurantId))
                .StatsChanged(redactedStats);

            await hubContext.Clients
                .Group(OrdersHub.RevenueGroup(restaurantId))
                .StatsChanged(fullStats);

            metrics.RecordPush("StatsChanged", 0);
        }
        catch (Exception ex)
        {
            metrics.RecordPushFailure("stats_changed");
            logger.LogWarning(
                ex,
                "Failed to broadcast StatsChanged for restaurant {RestaurantId}.",
                restaurantId);
        }
    }
}
