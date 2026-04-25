namespace Catalog.API.Models;

public class OrderItem : Entity<int>
{
    public Guid OrderId { get; set; }
    public Guid? MenuItemId { get; set; }
    public string MenuItemName { get; set; } = string.Empty;
    public string MenuItemDescription { get; set; } = string.Empty;
    public string MenuItemImageUrl { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal BasePrice { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    /// <summary>JSON snapshot of selected variations and their prices</summary>
    public string SelectedVariations { get; set; } = string.Empty; // jsonb
    
    /// <summary>JSON snapshot of additional customizations and extra charges</summary>
    public string Customizations { get; set; } = string.Empty; // jsonb
    
    /// <summary>Used for bill splitting by seat</summary>
    public int SeatNumber { get; set; }
    
    /// <summary>Preparation state: pending, preparing, ready</summary>
    public string PrepStatus { get; set; } = string.Empty;
    public Instant? PrepStartedAt { get; set; }
    public Instant? PrepCompletedAt { get; set; }
    public string SpecialInstructions { get; set; } = string.Empty;
    public Instant CreatedAt { get; set; }
}
