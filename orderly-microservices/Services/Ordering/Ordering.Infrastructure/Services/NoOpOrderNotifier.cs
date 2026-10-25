using Ordering.Application.Common.Interfaces;

namespace Ordering.Infrastructure.Services;

public sealed class NoOpOrderNotifier : IOrderNotifier
{
    public Task NotifyUpsertedAsync(
        IReadOnlyList<OrderChangeNotification> notifications,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyRemovedAsync(
        IReadOnlyList<OrderRemovalNotification> removals,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
