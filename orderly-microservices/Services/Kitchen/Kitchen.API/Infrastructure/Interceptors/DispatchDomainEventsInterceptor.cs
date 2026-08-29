namespace Kitchen.API.Infrastructure.Interceptors;

/// <summary>
/// Drains <c>IAggregate.DomainEvents</c> before <c>SaveChanges</c> commits,
/// dispatching each one through MediatR so application-level handlers run
/// in-process (logging, analytics). Mirrors Ordering's
/// <c>DispatchDomainEventsInterceptor</c> — dispatches BEFORE save so events
/// are not emitted when the transaction rolls back.
/// </summary>
public class DispatchDomainEventsInterceptor(IMediator mediator) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        DispatchDomainEvents(eventData.Context).GetAwaiter().GetResult();
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        await DispatchDomainEvents(eventData.Context, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private async Task DispatchDomainEvents(DbContext? context, CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            return;
        }

        List<IAggregate>? aggregates = context.ChangeTracker
            .Entries<IAggregate>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        List<IDomainEvent>? domainEvents = aggregates
            .SelectMany(a => a.DomainEvents)
            .ToList();

        aggregates.ForEach(a => a.ClearDomainEvents());

        foreach (IDomainEvent? domainEvent in domainEvents)
        {
            await mediator.Publish(domainEvent, cancellationToken);
        }
    }
}