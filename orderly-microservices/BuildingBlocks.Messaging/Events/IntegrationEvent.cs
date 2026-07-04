namespace BuildingBlocks.Messaging.Events;

public record IntegrationEvent
{
    // Captured once at construction. The previous getter expressions returned a
    // fresh value per read, so MassTransit would serialize one Guid on publish and
    // the consumer would deserialize a different one — defeating correlation and
    // idempotency.
    public Guid Id { get; init; } = Guid.NewGuid();
    public Instant OccurredOn { get; init; } = SystemClock.Instance.GetCurrentInstant();
    public string EventType => GetType().AssemblyQualifiedName!;
}
