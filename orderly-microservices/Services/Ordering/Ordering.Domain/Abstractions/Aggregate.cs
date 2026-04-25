namespace Ordering.Domain.Abstractions;

public abstract class Aggregate<T> : AuditableEntity<T>, IAggregate<T>
{
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public IDomainEvent[] ClearDomainEvents()
    {
        IDomainEvent[] domainEvents = _domainEvents.ToArray();

        _domainEvents.Clear();

        return domainEvents;
    }
}
