namespace Ordering.Domain.Models;

public class OrderItem : Entity<int>
{
    public decimal BasePrice { get; set; }
    public Instant CreatedAt { get; set; }
    /// <summary>JSON snapshot of additional customizations and extra charges</summary>
    public string Customizations { get; set; } = string.Empty; // jsonb
    public string MenuItemDescription { get; set; } = string.Empty;
    public string MenuItemImageUrl { get; set; } = string.Empty;
    public string MenuItemName { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    /// <summary>Preparation state: pending, preparing, ready</summary>
    public PrepStatus PrepStatus { get; set; } = PrepStatus.Pending;
    public int Quantity { get; set; }
    /// <summary>Used for bill splitting by seat</summary>
    public int SeatNumber { get; set; }
    /// <summary>JSON snapshot of selected variations and their prices</summary>
    public string SelectedVariations { get; set; } = string.Empty; // jsonb
    public string SpecialInstructions { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid? MenuItemId { get; set; }
    public Instant? PrepCompletedAt { get; set; }
    public Instant? PrepStartedAt { get; set; }
}
