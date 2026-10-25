namespace Ordering.Application.Common.Interfaces;

public sealed record OrderChangeNotification(
    OrderSummaryDto Order,
    string ChangeType,
    Instant OccurredAt);

public sealed record OrderRemovalNotification(
    Guid RestaurantId,
    Guid OrderId,
    Instant OccurredAt);

public interface IOrderNotifier
{
    Task NotifyUpsertedAsync(
        IReadOnlyList<OrderChangeNotification> notifications,
        CancellationToken cancellationToken = default);

    Task NotifyRemovedAsync(
        IReadOnlyList<OrderRemovalNotification> removals,
        CancellationToken cancellationToken = default);
}
