namespace Ordering.Domain.Abstractions;

public class Entity<T> : IEntity<T>
{
    public T Id { get; set; }
    public Instant? CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Instant? LastModified { get; set; }
    public Guid? LastModifiedBy { get; set; }
}
