using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ordering.Domain.Abstractions;

namespace Ordering.Infrastructure.Data.Interceptors;

public sealed class OrderingEntityAuditInterceptor(IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        StampEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void StampEntities(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        Instant timestamp = clock.GetCurrentInstant();
        HashSet<OrderId> touchedOrderIds = [];

        foreach (EntityEntry<IEntity> entry in context.ChangeTracker.Entries<IEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt ??= timestamp;
                entry.Entity.LastModified ??= entry.Entity.CreatedAt;
            }
            else if (entry.State == EntityState.Modified || HasChangedOwnedEntities(entry))
            {
                entry.Entity.LastModified = timestamp;
            }
        }

        foreach (EntityEntry<OrderItem> itemEntry in context.ChangeTracker.Entries<OrderItem>())
        {
            if (itemEntry.State is EntityState.Added or EntityState.Modified)
            {
                touchedOrderIds.Add(itemEntry.Entity.OrderId);
            }
        }

        if (touchedOrderIds.Count == 0)
        {
            return;
        }

        foreach (EntityEntry<Order> orderEntry in context.ChangeTracker.Entries<Order>())
        {
            if (touchedOrderIds.Contains(orderEntry.Entity.Id))
            {
                orderEntry.Entity.LastModified = timestamp;
            }
        }
    }

    private static bool HasChangedOwnedEntities(EntityEntry entry)
    {
        foreach (ReferenceEntry reference in entry.References)
        {
            EntityEntry? target = reference.TargetEntry;
            if (target is not null &&
                target.Metadata.IsOwned() &&
                target.State is EntityState.Added or EntityState.Modified)
            {
                return true;
            }
        }

        return false;
    }
}
