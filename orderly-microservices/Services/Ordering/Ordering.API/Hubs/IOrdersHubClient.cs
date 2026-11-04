using NodaTime;
using Ordering.Application.Dashboard.Dtos;
using Ordering.Application.Dtos;

namespace Ordering.API.Hubs;

public interface IOrdersHubClient
{
    Task OrderUpserted(OrderSummaryDto order, string changeType, Instant occurredAt);

    Task OrderRemoved(Guid orderId);

    Task StatsChanged(OrdersStatsDto stats);

    Task DashboardChanged(DashboardChangedDto notification);
}
