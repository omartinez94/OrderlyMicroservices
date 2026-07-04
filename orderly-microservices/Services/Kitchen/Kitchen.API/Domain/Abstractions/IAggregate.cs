using BuildingBlocks.Entities.Interfaces;

namespace Kitchen.API.Domain.Abstractions;

/// <summary>
/// Non-generic aggregate contract. Lets the EF Core interceptor query every
/// tracked aggregate via <c>Entries&lt;IAggregate&gt;()</c> without caring
/// about the strongly-typed identifier — mirrors
/// <c>Ordering.Domain.Abstractions.IAggregate</c>.
/// </summary>
public interface IAggregate : IEntity
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    IDomainEvent[] ClearDomainEvents();
}

/// <summary>
/// Strongly-typed aggregate root. <see cref="AddDomainEvent"/> is not part of
/// the contract on purpose — only the concrete <see cref="Aggregate{TId}"/>
/// base needs to enqueue events; external code reads them through
/// <see cref="DomainEvents"/> / <see cref="ClearDomainEvents"/>.
/// </summary>
public interface IAggregate<TId> : IAggregate, IEntity<TId>
{
}