namespace Catalog.API.Models;

public class MenuCategory : AuditableEntity<int>
{
    public string Description { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsDeleted { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RestaurantId { get; set; }
    public Instant? DeletedAt { get; set; }
}
