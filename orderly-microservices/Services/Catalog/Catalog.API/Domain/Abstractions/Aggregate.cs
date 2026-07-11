namespace Catalog.API.Domain.Abstractions;

/// <summary>
/// Base class for Catalog aggregates that don't need audit fields. Inherits
/// <c>BuildingBlocks.Entities.Contracts.Entity&lt;TId&gt;</c> (which provides
/// <c>TId Id</c>) and implements <see cref="IAggregate{TId}"/>.
/// </summary>
/// <remarks>
/// Used by <c>IngredientAlternative</c> and <c>MenuItemIngredient</c> (both
/// previously extended <c>Entity&lt;int&gt;</c>). <c>Ingredient</c> uses
/// <see cref="AuditableAggregate{TId}"/> instead so it preserves the
/// <c>CreatedAt</c> / <c>LastModifiedAt</c> / <c>IsActive</c> audit columns
/// that <c>AuditableEntityInterceptor</c> already populates.
/// </remarks>
public abstract class Aggregate<TId> : Entity<TId>, IAggregate<TId>
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