using BuildingBlocks.Entities.Interfaces;
using NodaTime;
using System.Text.Json.Serialization;

namespace BuildingBlocks.Entities.Contracts;

public abstract class AuditableEntity<TId> : IAuditableEntity<TId>
{
    public TId Id { get; set; } = default!;
    public string CreatedBy { get; protected set; } = default!;
    public Instant CreatedAt { get; protected set; }
    public string LastModifiedBy { get; protected set; } = default!;
    public Instant? LastModifiedAt { get; protected set; } = null!;
    public bool IsActive { get; protected set; }

    /// <summary>
    /// Default constructor
    /// </summary>
    protected AuditableEntity()
    {/* ... */}

    [JsonConstructor]
    protected AuditableEntity(TId id, string createdBy, Instant createdAt, string lastModifiedBy, Instant? lastModifiedAt,
        bool isActive)
    {
        Id = id;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        LastModifiedBy = lastModifiedBy;
        LastModifiedAt = lastModifiedAt;
        IsActive = isActive;
    }

    void IAuditableEntity.CreatedFrom(string userId, Instant timestamp)
    {
        CreatedBy = userId;
        CreatedAt = timestamp;
        LastModifiedBy = userId;
        LastModifiedAt = timestamp;
    }

    void IAuditableEntity.ModifiedFrom(string userId, Instant timestamp)
    {
        LastModifiedBy = userId;
        LastModifiedAt = timestamp;
    }
}
