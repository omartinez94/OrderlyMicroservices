namespace Ordering.Application.Common.Interfaces;

using Ordering.Application.Dashboard.Dtos;

public interface IDashboardSummaryService
{
    Task<RestaurantOperationsDashboardDto> GetLiveSummaryAsync(
        Guid restaurantId,
        bool includeRevenue,
        CancellationToken cancellationToken = default);
}
