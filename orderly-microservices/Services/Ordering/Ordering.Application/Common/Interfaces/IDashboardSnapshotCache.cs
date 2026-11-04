namespace Ordering.Application.Common.Interfaces;

using Ordering.Application.Dashboard.Dtos;

public interface IDashboardSnapshotCache
{
    Task<RestaurantOperationsDashboardDto> GetOrComputeAsync(
        Guid restaurantId,
        bool includeRevenue,
        Func<Task<RestaurantOperationsDashboardDto>> computeFactory,
        CancellationToken cancellationToken = default);

    Task InvalidateAsync(
        Guid restaurantId,
        IReadOnlyList<string>? changedSections = null,
        CancellationToken cancellationToken = default);
}
