namespace Catalog.API.Models;

public class MenuSubCategory : Entity<int>
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public Instant? DeletedAt { get; set; }
}
