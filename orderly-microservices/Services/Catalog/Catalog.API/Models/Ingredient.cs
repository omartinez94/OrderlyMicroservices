namespace Catalog.API.Models;

public class Ingredient : AuditableEntity<int>
{
    public decimal CurrentStock { get; set; }
    public bool IsAvailable { get; set; }
    public decimal MinimumStock { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RestaurantId { get; set; }
    /// <summary>Unit of measurement: kg, liters, units, etc.</summary>
    public string Unit { get; set; } = string.Empty;
}
