namespace Catalog.API.Models;

public class MenuItemVariation : Entity<int>
{
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsDeleted { get; set; }
    public Guid MenuItemId { get; set; }
    /// <summary>Name of the variation category (e.g., Size, Spice Level)</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Price adjustment when this variation is selected (can be negative or positive)</summary>
    public decimal PriceModifier { get; set; }
    /// <summary>Specific value for this variation (e.g., Large, Extra Spicy)</summary>
    public string VariationValue { get; set; } = string.Empty;
    public Instant? DeletedAt { get; set; }
}
