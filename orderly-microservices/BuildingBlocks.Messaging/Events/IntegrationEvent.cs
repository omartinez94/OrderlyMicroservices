using NodaTime;

namespace BuildingBlocks.Messaging.Events;

public record IntegrationEvent
{
    public Guid Id => Guid.NewGuid();
    public Instant OccurredOn => SystemClock.Instance.GetCurrentInstant();
    public string EventType => GetType().AssemblyQualifiedName;
}
