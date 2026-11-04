namespace Ordering.Application.Dashboard.Queries.GetRestaurantDashboardLiveSummary;

public record GetRestaurantDashboardLiveSummaryQuery(Guid RestaurantId, bool IncludeRevenue = false)
    : IQuery<GetRestaurantDashboardLiveSummaryResult>;

public record GetRestaurantDashboardLiveSummaryResult(RestaurantOperationsDashboardDto Dashboard);
