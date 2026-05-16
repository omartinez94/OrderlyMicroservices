namespace Identity.API.Models;

public class UserRestaurant
{
    public required Guid UserId { get; set; }
    public required ApplicationUser User { get; set; }
    public required int RestaurantId { get; set; }
    public bool IsDefault { get; set; }
}
