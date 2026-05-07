namespace Identity.API.Models;

public class UserRestaurant
{
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public int RestaurantId { get; set; }
    public bool IsDefault { get; set; }
}
