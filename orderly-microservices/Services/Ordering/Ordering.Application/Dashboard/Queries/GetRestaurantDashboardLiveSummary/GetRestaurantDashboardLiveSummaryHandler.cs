namespace Ordering.Application.Dashboard.Queries.GetRestaurantDashboardLiveSummary;

public class GetRestaurantDashboardLiveSummaryHandler(
    IDashboardSummaryService dashboardSummaryService)
    : IQueryHandler<GetRestaurantDashboardLiveSummaryQuery, GetRestaurantDashboardLiveSummaryResult>
{
    public async Task<GetRestaurantDashboardLiveSummaryResult> Handle(
        GetRestaurantDashboardLiveSummaryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        RestaurantOperationsDashboardDto dashboard = await dashboardSummaryService.GetLiveSummaryAsync(
            query.RestaurantId,
            query.IncludeRevenue,
            cancellationToken);

        return new GetRestaurantDashboardLiveSummaryResult(dashboard);
    }
}
