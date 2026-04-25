namespace Catalog.API.Models;

public class MenuItem : AuditableEntity<Guid>
{
    /// <summary>Current availability: available, limited, unavailable</summary>
    public AvailabilityStatus AvailabilityStatus { get; set; } = AvailabilityStatus.Available;
    public decimal BasePrice { get; set; }
    public string Description { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool IsDeleted { get; set; }
    /// <summary>Type of item: regular, combo, promo, seasonal</summary>
    public ItemType ItemType { get; set; } = ItemType.Regular;
    public string Name { get; set; } = string.Empty;
    public int PrepTimeMaxMinutes { get; set; }
    public int PrepTimeMinutes { get; set; }
    public Guid RestaurantId { get; set; }
    public Instant? DeletedAt { get; set; }
    public Instant? PromoEndDate { get; set; }
    public decimal? PromoPrice { get; set; }
    public Instant? PromoStartDate { get; set; }
    public LocalDate? SeasonEndDate { get; set; }
    public LocalDate? SeasonStartDate { get; set; }
    public int? SubCategoryId { get; set; }
}
