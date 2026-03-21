using BuildingBlocks.Entities.Interfaces;
using NodaTime;
using System.Text.Json.Serialization;

namespace BuildingBlocks.Entities.Contracts;

public abstract class AuditableEntity<TId> : IAuditableEntity<TId>, IDeletableEntity
{
    public TId Id { get; set; }
    public string CreatedBy { get; protected set; }
    public Instant CreatedOn { get; protected set; }
    public string LastModifiedBy { get; protected set; }
    public Instant? LastModifiedOn { get; protected set; } = null!;
    public bool IsActive { get; protected set; }

    /// <summary>
    /// Default constructor
    /// </summary>
    protected AuditableEntity()
    {/* ... */}

    [JsonConstructor]
    protected AuditableEntity(TId id, string createdBy, Instant createdOn, string lastModifiedBy, Instant? lastModifiedOn,
        bool isActive)
    {
        Id = id;
        CreatedBy = createdBy;
        CreatedOn = createdOn;
        LastModifiedBy = lastModifiedBy;
        LastModifiedOn = lastModifiedOn;
        IsActive = isActive;
    }

    public void Delete() => IsActive = false;

    /// <summary>
    /// Undo soft delete of entity.
    /// </summary>
    public void Undelete() => IsActive = true;

    void IAuditableEntity.CreatedFrom(string userId, Instant timestamp)
    {
        CreatedBy = userId;
        CreatedOn = timestamp;
    }

    void IAuditableEntity.ModifiedFrom(string userId, Instant timestamp)
    {
        LastModifiedBy = userId;
        LastModifiedOn = timestamp;
    }
}
