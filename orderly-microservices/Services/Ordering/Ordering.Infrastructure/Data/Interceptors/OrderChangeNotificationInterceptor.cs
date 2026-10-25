using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Dtos;
using Ordering.Application.Extensions;

namespace Ordering.Infrastructure.Data.Interceptors;

public sealed class OrderChangeNotificationInterceptor(
    IOrderNotifier orderNotifier,
    IClock clock,
    ILogger<OrderChangeNotificationInterceptor> logger)
    : SaveChangesInterceptor
{
    public const string ChangeTypeCreated = "Created";
    public const string ChangeTypeCancelled = "Cancelled";
    public const string ChangeTypeStatusChanged = "StatusChanged";
    public const string ChangeTypeItemPrepChanged = "ItemPrepChanged";
    public const string ChangeTypeUpdated = "Updated";

    private readonly ConcurrentDictionary<DbContextId, PendingNotificationBatch> _pendingByContext = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        CapturePendingChanges(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CapturePendingChanges(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        if (eventData.Context is not null &&
            _pendingByContext.TryRemove(eventData.Context.ContextId, out PendingNotificationBatch? batch))
        {
            DispatchNotificationsAsync(eventData.Context, batch, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null &&
            _pendingByContext.TryRemove(eventData.Context.ContextId, out PendingNotificationBatch? batch))
        {
            await DispatchNotificationsAsync(eventData.Context, batch, cancellationToken);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is not null)
        {
            _pendingByContext.TryRemove(eventData.Context.ContextId, out _);
        }

        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            _pendingByContext.TryRemove(eventData.Context.ContextId, out _);
        }

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void CapturePendingChanges(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        Dictionary<OrderId, string> upserts = [];
        List<OrderRemovalNotification> removals = [];
        Instant now = clock.GetCurrentInstant();

        CaptureOrderEntries(context, upserts, removals, now);
        CaptureOrderItemEntries(context, upserts);

        if (upserts.Count == 0 && removals.Count == 0)
        {
            _pendingByContext.TryRemove(context.ContextId, out _);
            return;
        }

        _pendingByContext[context.ContextId] = new PendingNotificationBatch(
            upserts,
            removals,
            now);
    }

    private static void CaptureOrderEntries(
        DbContext context,
        Dictionary<OrderId, string> upserts,
        List<OrderRemovalNotification> removals,
        Instant occurredAt)
    {
        foreach (EntityEntry<Order> entry in context.ChangeTracker.Entries<Order>())
        {
            if (entry.State == EntityState.Added)
            {
                upserts[entry.Entity.Id] = ChangeTypeCreated;
            }
            else if (entry.State == EntityState.Deleted)
            {
                removals.Add(new OrderRemovalNotification(
                    entry.Entity.RestaurantId,
                    entry.Entity.Id.Value,
                    occurredAt));
            }
            else if (entry.State == EntityState.Modified)
            {
                upserts[entry.Entity.Id] = ResolveModifiedOrderChangeType(entry);
            }
        }
    }

    private static string ResolveModifiedOrderChangeType(EntityEntry<Order> entry)
    {
        PropertyEntry<Order, OrderStatus> statusProp = entry.Property(o => o.Status);
        if (statusProp.IsModified && !Equals(statusProp.OriginalValue, statusProp.CurrentValue))
        {
            return entry.Entity.Status == OrderStatus.Cancelled
                ? ChangeTypeCancelled
                : ChangeTypeStatusChanged;
        }

        return ChangeTypeUpdated;
    }

    private static void CaptureOrderItemEntries(
        DbContext context,
        Dictionary<OrderId, string> upserts)
    {
        foreach (EntityEntry<OrderItem> itemEntry in context.ChangeTracker.Entries<OrderItem>())
        {
            if (itemEntry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            OrderId orderId = itemEntry.Entity.OrderId;
            bool prepModified = itemEntry.State == EntityState.Modified &&
                (itemEntry.Property(i => i.PrepStatus).IsModified ||
                 itemEntry.Property(i => i.PrepStartedAt).IsModified ||
                 itemEntry.Property(i => i.PrepCompletedAt).IsModified);

            if (!upserts.TryGetValue(orderId, out string? existingChangeType))
            {
                upserts[orderId] = prepModified ? ChangeTypeItemPrepChanged : ChangeTypeUpdated;
            }
            else if (existingChangeType == ChangeTypeUpdated && prepModified)
            {
                upserts[orderId] = ChangeTypeItemPrepChanged;
            }
        }
    }

    private async Task DispatchNotificationsAsync(
        DbContext context,
        PendingNotificationBatch batch,
        CancellationToken cancellationToken)
    {
        try
        {
            if (batch.Upserts.Count > 0)
            {
                Dictionary<OrderId, Order> trackedOrders = context.ChangeTracker
                    .Entries<Order>()
                    .Where(e => e.State != EntityState.Detached && e.State != EntityState.Deleted)
                    .ToDictionary(e => e.Entity.Id, e => e.Entity);

                List<OrderChangeNotification> notifications = [];
                foreach ((OrderId orderId, string changeType) in batch.Upserts)
                {
                    if (!trackedOrders.TryGetValue(orderId, out Order? order))
                    {
                        order = await context.Set<Order>()
                            .IgnoreQueryFilters()
                            .Include(o => o.OrderItems)
                            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
                    }
                    else if (!context.Entry(order).Collection(o => o.OrderItems).IsLoaded &&
                             order.OrderItems.Count == 0)
                    {
                        await context.Entry(order)
                            .Collection(o => o.OrderItems)
                            .LoadAsync(cancellationToken);
                    }

                    if (order is null || order.Status == OrderStatus.Ordering)
                    {
                        continue;
                    }

                    OrderSummaryDto dto = order.ToOrderSummaryDto();
                    notifications.Add(new OrderChangeNotification(dto, changeType, batch.OccurredAt));
                }

                if (notifications.Count > 0)
                {
                    await orderNotifier.NotifyUpsertedAsync(notifications, cancellationToken);
                }
            }

            if (batch.Removals.Count > 0)
            {
                await orderNotifier.NotifyRemovedAsync(batch.Removals, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Post-commit order change notification failed; committed database state is unaffected.");
        }
    }

    private sealed record PendingNotificationBatch(
        IReadOnlyDictionary<OrderId, string> Upserts,
        IReadOnlyList<OrderRemovalNotification> Removals,
        Instant OccurredAt);
}
