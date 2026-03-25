using BuildingBlocks.Entities.Interfaces;

namespace BuildingBlocks.Entities.Contracts;

public class Entity<TId> : IEntity<TId>
{
    public required TId Id { get; set; }
}
