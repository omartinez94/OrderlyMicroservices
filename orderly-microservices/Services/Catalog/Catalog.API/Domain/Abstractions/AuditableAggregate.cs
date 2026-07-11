namespace Catalog.API.Domain.Abstractions;

/// <summary>
/// Base class for Catalog aggregates that need audit fields. Combines
/// <see cref="BuildingBlocks.Entities.Contracts.AuditableEntity{TId}"/>
/// (which provides <c>CreatedAt</c> / <c>LastModifiedAt</c> / <c>IsActive</c>)
/// with the <see cref="IAggregate{TId}"/> domain-event contract.
/// </summary>
/// <remarks>
/// Used by <c>Ingredient</c>, which previously extended
/// <c>AuditableEntity&lt;int&gt;</c>. Keeping the audit fields means
/// <c>AuditableEntityInterceptor</c> continues to stamp <c>LastModifiedAt</c>
/// on every save with no schema migration.
/// </remarks>
public abstract class AuditableAggregate<TId> : AuditableEntity<TId>, IAggregate<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <inheritdoc/>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <inheritdoc/>
    public void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <inheritdoc/>
    public IDomainEvent[] ClearDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }
}