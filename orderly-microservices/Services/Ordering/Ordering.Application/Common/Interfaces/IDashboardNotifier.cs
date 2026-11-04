namespace Ordering.Application.Common.Interfaces;

public interface IDashboardNotifier
{
    Task NotifyDashboardChangedAsync(
        Guid restaurantId,
        IReadOnlyList<string> changedSections,
        CancellationToken cancellationToken = default);
}
