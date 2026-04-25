namespace Ordering.Domain.Abstractions;

public interface IDomainEvent : INotification
{
    Guid EventId => Guid.NewGuid();
    public Instant OccurredOn { get; set; }
    public string EventType => GetType().AssemblyQualifiedName;
}
