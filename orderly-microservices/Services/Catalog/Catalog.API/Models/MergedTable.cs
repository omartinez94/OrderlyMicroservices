namespace Catalog.API.Models;

public class MergedTable : Entity<Guid>
{
    public Guid ParentTableId { get; set; }
    public Guid ChildTableId { get; set; }
    public Instant MergedAt { get; set; }
    public Instant? SplitAt { get; set; }
    public bool IsActive { get; set; }
}
