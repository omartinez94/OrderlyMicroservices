namespace Ordering.Domain.Abstractions;

public interface IEntity<T> : IEntity
{
    public T Id { get; set; }
}

public interface IEntity
{
    public Instant? CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Instant? LastModified { get; set; }
    public Guid? LastModifiedBy { get; set; }
}
