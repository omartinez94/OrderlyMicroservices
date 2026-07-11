using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Catalog.API.Infrastructure.Interceptors;

/// <summary>
/// Pre-commit EF Core interceptor that drains
/// <see cref="IAggregate.DomainEvents"/> via
/// <see cref="IMediator.Publish"/> before the aggregate transaction
/// commits. Mirrors the pattern used by Ordering's
/// <c>Ordering.Infrastructure/Data/Interceptors/DispatchDomainEventsInterceptor</c>
/// and Kitchen's
/// <c>Kitchen.API/Infrastructure/Interceptors/DispatchDomainEventsInterceptor</c>
/// (Kitchen's variant is the closer template — also Npgsql, single
/// <c>*.API</c> project, reuses BuildingBlocks' <c>Entity&lt;TId&gt;</c>).
/// </summary>
/// <remarks>
/// <para><b>Pre-commit ordering.</b> The interceptor runs in
/// <see cref="SavingChangesAsync(DbContextEventData, InterceptionResult{int}, CancellationToken)"/>
/// — <em>before</em> the actual SQL write. Domain-event handlers
/// therefore observe the aggregate in its pre-commit state. If the
/// transaction rolls back, the events have already fired downstream
/// (e.g. <c>IngredientAvailabilityChangedIntegrationEvent</c> may have
/// been staged on the outbox). The transactional outbox pattern
/// ensures the staged row only publishes if the transaction
/// commits.</para>
/// <para><b>Sync vs async.</b> Both overrides are implemented. The
/// async path is the hot one; the sync override blocks on it. The
/// <see cref="DispatchDomainEvents"/> helper is the single source of
/// truth — both overrides call it.</para>
/// </remarks>
public sealed class DispatchDomainEventsInterceptor(IMediator mediator) : SaveChangesInterceptor
{
    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        DispatchDomainEventsAsync(eventData.Context, cancellationToken: default)
            .GetAwaiter()
            .GetResult();

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        await DispatchDomainEventsAsync(eventData.Context, cancellationToken).ConfigureAwait(false);

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchDomainEventsAsync(DbContext? context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);

        if (context is null)
        {
            return;
        }

        // Snapshot aggregates that have pending domain events. The
        // `Entries<IAggregate>()` filter picks up every tracked aggregate
        // in the current DbContext (Entity Framework's tracker is the
        // single source of truth for "what changed in this SaveChanges").
        var aggregates = context.ChangeTracker
            .Entries<IAggregate>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (aggregates.Count == 0)
        {
            return;
        }

        // ClearDomainEvents first so handlers that mutate the same aggregate
        // (e.g. cascading MenuItem updates from an Ingredient event) don't
        // re-publish the same event when they themselves call SaveChanges.
        // The snapshot is taken AFTER clear so we publish the original list.
        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        foreach (var domainEvent in events)
        {
            await mediator.Publish(domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}