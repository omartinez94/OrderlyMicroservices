using BuildingBlocks.Messaging.Events.Ordering;
using BuildingBlocks.Messaging.Outbox;
using Microsoft.Extensions.Logging;
using Ordering.Application.Data;

namespace Ordering.Infrastructure.Services;

/// <summary>
/// Daily reconciliation runner: finds stale orders stuck in <see cref="OrderStatus.Pending"/>
/// for 15+ minutes and cancels them, staging <see cref="OrderCancelledIntegrationEvent"/> in the outbox.
/// </summary>
public sealed class DailyReconciliationRunner(
    IApplicationDbContext dbContext,
    IClock clock,
    IOutboxPublisher outboxPublisher,
    ILogger<DailyReconciliationRunner> logger)
    : IDailyReconciliationRunner
{
    public const int StalePendingTimeoutMinutes = 15;
    public const string StalePendingCancellationReason = "Stale pending order expired by daily reconciliation pass";

    /// <inheritdoc />
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Daily reconciliation triggered; querying stale pending orders.");

        if (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }

        Instant serverTime = clock.GetCurrentInstant();
        Instant cutoff = serverTime - Duration.FromMinutes(StalePendingTimeoutMinutes);

        List<Order> staleOrders;
        try
        {
            staleOrders = await dbContext.Orders
                .Where(o => o.Status == OrderStatus.Pending && o.CreatedAt.HasValue && o.CreatedAt.Value <= cutoff)
                .ToListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }

        if (staleOrders.Count == 0)
        {
            logger.LogInformation("Daily reconciliation completed: 0 stale pending orders found.");
            return 0;
        }

        foreach (Order order in staleOrders)
        {
            order.Cancel(serverTime, StalePendingCancellationReason);

            OrderCancelledIntegrationEvent integrationEvent = new()
            {
                OrderId = order.Id.Value,
                RestaurantId = order.RestaurantId,
                OrderNumber = order.OrderNumber.Value,
                CancelledAt = order.CancelledAt ?? serverTime,
                CancelledByUserId = order.CancelledByUserId ?? Order.SystemUserId,
                Reason = StalePendingCancellationReason
            };

            await outboxPublisher.PublishAsync(integrationEvent, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Daily reconciliation completed: {ReconciledCount} stale pending orders reconciled.",
            staleOrders.Count);

        return staleOrders.Count;
    }
}
