namespace Kitchen.API.Domain.Abstractions;

/// <summary>
/// Base aggregate root. Mirrors <c>Ordering.Domain.Abstractions.Aggregate&lt;T&gt;</c>.
/// Holds the in-process event list and exposes <c>ClearDomainEvents</c> for
/// the EF Core interceptor to drain after <c>SaveChangesAsync</c>.
/// </summary>
public abstract class Aggregate<TId> : Entity<TId>, IAggregate<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    public IDomainEvent[] ClearDomainEvents()
    {
        IDomainEvent[] dequeued = _domainEvents.ToArray();
        _domainEvents.Clear();
        return dequeued;
    }
}