namespace Basket.API.Models;

public class BasketItem
{
    public int MenuItemId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public List<BasketItemVariation> Variations { get; set; } = new();
    public List<BasketItemCustomization> Customizations { get; set; } = new();
    
    public decimal TotalPrice => (UnitPrice + Variations.Sum(v => v.Price)) * Quantity;
}

public class BasketItemVariation
{
    public string Name { get; set; } = default!;
    public string Value { get; set; } = default!;
    public decimal Price { get; set; }
}

public class BasketItemCustomization
{
    public string Ingredient { get; set; } = default!;
    public string Action { get; set; } = default!;
}
