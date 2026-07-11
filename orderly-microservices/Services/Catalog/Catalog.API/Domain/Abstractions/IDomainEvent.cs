namespace Catalog.API.Domain.Abstractions;

/// <summary>
/// Marker interface for in-process domain events raised by Catalog
/// aggregates and dispatched by
/// <c>Catalog.API.Infrastructure.Interceptors.DispatchDomainEventsInterceptor</c>
/// before the aggregate transaction commits.
/// </summary>
/// <remarks>
/// <para><b>Stable identity (init-only).</b> Mirrors Kitchen's
/// <c>IDomainEvent</c> rather than Ordering's. Ordering's
/// <c>Guid.NewGuid() =&gt; EventId</c> default getter re-evaluates on every
/// read, so MassTransit serializes one Guid on publish and consumers
/// deserialize a different one — defeating correlation. The
/// <c>init</c>-only properties here capture the value once at
/// construction and return it consistently thereafter.</para>
/// <para><b>Service-local duplication.</b> Neither Ordering nor Kitchen
/// share <c>IDomainEvent</c> through BuildingBlocks. Catalog follows the
/// same per-service duplication. Only <c>BuildingBlocks.Entities</c>
/// (the bare <c>IEntity</c> / <c>Entity&lt;TId&gt;</c> base classes) is
/// reused.</para>
/// </remarks>
public interface IDomainEvent : INotification
{
    /// <summary>
    /// Stable per-instance correlation id. Set once at construction; never
    /// re-evaluates.
    /// </summary>
    Guid EventId { get; init; }

    /// <summary>When the event was raised (set once at construction).</summary>
    Instant OccurredOn { get; init; }

    /// <summary>CLR type discriminator (used by the dispatcher for logging).</summary>
    string EventType => GetType().AssemblyQualifiedName!;
}