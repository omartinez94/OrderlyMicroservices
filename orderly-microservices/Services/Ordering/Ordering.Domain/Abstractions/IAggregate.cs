namespace Ordering.Domain.Abstractions;

public interface IAggregate<T> : IAggregate, IAuditableEntity<T>
{
}

public interface IAggregate : IAuditableEntity
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    IDomainEvent[] ClearDomainEvents();
}
