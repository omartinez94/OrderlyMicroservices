namespace Ordering.Domain.Abstractions;

public interface IDomainEvent : INotification
{
    Guid EventId => Guid.NewGuid();
    public Instant OccurredOn => SystemClock.Instance.GetCurrentInstant();
    public string EventType => GetType().AssemblyQualifiedName;
}
