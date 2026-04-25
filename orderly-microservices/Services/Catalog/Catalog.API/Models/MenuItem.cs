namespace Catalog.API.Models;

public class MenuItem : AuditableEntity<Guid>
{
    public Guid RestaurantId { get; set; }
    public int? SubCategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public int PrepTimeMinutes { get; set; }
    public int PrepTimeMaxMinutes { get; set; }
    /// <summary>Type of item: regular, combo, promo, seasonal</summary>
    public string ItemType { get; set; } = string.Empty; // regular,combo,promo,seasonal
    
    public bool IsAvailable { get; set; }
    
    /// <summary>Current availability: available, limited, unavailable</summary>
    public string AvailabilityStatus { get; set; } = string.Empty;
    public LocalDate? SeasonStartDate { get; set; }
    public LocalDate? SeasonEndDate { get; set; }
    public decimal? PromoPrice { get; set; }
    public Instant? PromoStartDate { get; set; }
    public Instant? PromoEndDate { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDeleted { get; set; }
    public Instant? DeletedAt { get; set; }
}
