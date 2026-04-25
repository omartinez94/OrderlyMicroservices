namespace Catalog.API.Models;

public class PriceHistory : Entity<int>
{
    public Guid RestaurantId { get; set; }
    public Guid? MenuItemId { get; set; }
    public int? IngredientAlternativeId { get; set; }
    public int? VariationId { get; set; }
    public PriceType PriceType { get; set; } = PriceType.BasePrice;
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid ChangedByUserId { get; set; }
    public Instant EffectiveDate { get; set; }
    public Instant CreatedAt { get; set; }
}
