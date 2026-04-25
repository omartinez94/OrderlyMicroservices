namespace Catalog.API.Models;

public class MenuItemIngredient : Entity<int>
{
    public Guid MenuItemId { get; set; }
    public int IngredientId { get; set; }
    public decimal QuantityRequired { get; set; }
    public bool IsOptional { get; set; }
}
