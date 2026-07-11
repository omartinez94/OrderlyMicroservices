using BuildingBlocks.Entities.Interfaces;

namespace Catalog.API.Domain.Abstractions;

/// <summary>
/// Catalog aggregate root — extends the bare BuildingBlocks
/// <see cref="IEntity"/> with a domain-event collection that the
/// <c>DispatchDomainEventsInterceptor</c> drains on <c>SaveChanges</c>.
/// </summary>
/// <remarks>
/// Mirror of <c>Services/Kitchen/Kitchen.API/Domain/Abstractions/IAggregate.cs</c>
/// (Kitch is the closer template — also Npgsql, single <c>*.API</c>
/// project, reuses <c>BuildingBlocks.Entities</c>). Ordering's variant
/// is identical in shape but redefines <c>IEntity</c> locally; Catalog
/// reuses BuildingBlocks to avoid that duplication.
/// </remarks>
public interface IAggregate : IEntity
{
    /// <summary>
    /// Domain events accumulated via <see cref="AddDomainEvent"/>. The
    /// dispatcher reads this list and then calls <see cref="ClearDomainEvents"/>
    /// so each event fires exactly once.
    /// </summary>
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    /// <summary>Adds <paramref name="domainEvent"/> to the queue.</summary>
    void AddDomainEvent(IDomainEvent domainEvent);

    /// <summary>
    /// Snapshots the current domain-event list, clears the queue, and returns
    /// the snapshot. Called by the interceptor after the events have been
    /// dispatched.
    /// </summary>
    IDomainEvent[] ClearDomainEvents();
}

/// <summary>Typed aggregate root (carries a strongly-typed id).</summary>
public interface IAggregate<TId> : IAggregate, IEntity<TId>;