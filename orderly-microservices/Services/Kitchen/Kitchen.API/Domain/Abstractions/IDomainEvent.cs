namespace Kitchen.API.Domain.Abstractions;

/// <summary>
/// Marker interface for in-process domain events raised by aggregates. The
/// <c>DispatchDomainEventsInterceptor</c> dispatches them via MediatR after
/// each <c>SaveChanges</c> so application-level handlers can run in the
/// same transaction. Mirrors <c>Ordering.Domain.Abstractions.IDomainEvent</c>.
///
/// <c>EventId</c> and <c>OccurredOn</c> are <c>init</c>-only properties with
/// no interface default — each implementing record must initialise them so
/// the values are stable per instance. The previous <c>get; =&gt; ...</c>
/// default accessors re-evaluated on every read.
/// <para>
/// <c>EventType</c> stays a default <c>get;</c> accessor because it is
/// intrinsically derived from <c>GetType()</c>.
/// </para>
/// </summary>
public interface IDomainEvent : INotification
{
    Guid EventId { get; init; }

    Instant OccurredOn { get; init; }

    string EventType => GetType().AssemblyQualifiedName!;
}