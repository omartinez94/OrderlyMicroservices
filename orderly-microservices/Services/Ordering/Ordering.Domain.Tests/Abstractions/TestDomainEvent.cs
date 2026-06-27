namespace Ordering.Domain.Tests.Abstractions;

/// <summary>
/// Minimal IDomainEvent for tests. Avoids pulling a real domain event type into the
/// aggregate tests so the assertions stay focused on Aggregate's behavior.
/// </summary>
internal sealed record TestDomainEvent(Guid EventId) : IDomainEvent
{
    public Instant OccurredOn => SystemClock.Instance.GetCurrentInstant();
    public string EventType => GetType().AssemblyQualifiedName!;
}