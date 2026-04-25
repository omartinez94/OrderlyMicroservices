namespace Catalog.API.Models;

public class ComboItem : Entity<int>
{
    public Guid ComboMenuItemId { get; set; }
    public Guid IncludedMenuItemId { get; set; }
    public bool IsOptional { get; set; }
    public int Quantity { get; set; }
}
