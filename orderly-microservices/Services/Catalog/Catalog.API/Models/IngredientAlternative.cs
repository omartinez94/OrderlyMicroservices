namespace Catalog.API.Models;

public class IngredientAlternative : Entity<int>
{
    public int OriginalIngredientId { get; set; }
    public int AlternativeIngredientId { get; set; }
    /// <summary>Price adjustment when this alternative is used (e.g. +$1.00 for gluten-free bun)</summary>
    public decimal PriceModifier { get; set; }
    public bool AutoSubstitute { get; set; }
    public Guid RestaurantId { get; set; }
}
