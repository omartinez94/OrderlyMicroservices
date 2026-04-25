namespace Catalog.API.Models;

public class MenuItemIngredient : Entity<int>
{
    public int IngredientId { get; set; }
    public bool IsOptional { get; set; }
    public Guid MenuItemId { get; set; }
    public decimal QuantityRequired { get; set; }
}
