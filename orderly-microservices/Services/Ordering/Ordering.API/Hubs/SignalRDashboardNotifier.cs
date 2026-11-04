namespace Ordering.API.Hubs;

using Microsoft.AspNetCore.SignalR;
using NodaTime;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Dashboard.Dtos;

public sealed class SignalRDashboardNotifier(
    IHubContext<OrdersHub, IOrdersHubClient> hubContext,
    IClock clock,
    ILogger<SignalRDashboardNotifier> logger)
    : IDashboardNotifier
{
    public async Task NotifyDashboardChangedAsync(
        Guid restaurantId,
        IReadOnlyList<string> changedSections,
        CancellationToken cancellationToken = default)
    {
        Instant now = clock.GetCurrentInstant();
        DashboardChangedDto dto = new(restaurantId, changedSections, now);

        try
        {
            await hubContext.Clients
                .Groups([OrdersHub.StatsGroup(restaurantId), OrdersHub.RevenueGroup(restaurantId)])
                .DashboardChanged(dto);

            logger.LogDebug("Dispatched DashboardChanged notification for restaurant {RestaurantId}", restaurantId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to dispatch DashboardChanged SignalR notification for restaurant {RestaurantId}", restaurantId);
        }
    }
}
