using NodaTime;

namespace BuildingBlocks.Entities.Interfaces;

public interface IAuditableEntity<TId> : IAuditableEntity, IEntity<TId>
{
}

public interface IAuditableEntity : IEntity
{
    string CreatedBy { get; }

    Instant CreatedOn { get; }

    string LastModifiedBy { get; }

    Instant? LastModifiedOn { get; }

    void CreatedFrom(string userId, Instant timestamp);

    void ModifiedFrom(string userId, Instant timestamp);
}