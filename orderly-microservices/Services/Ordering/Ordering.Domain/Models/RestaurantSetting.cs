namespace Ordering.Domain.Models;

public class RestaurantSetting
{
    public Guid RestaurantId { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public Instant UpdatedAt { get; set; }
}
