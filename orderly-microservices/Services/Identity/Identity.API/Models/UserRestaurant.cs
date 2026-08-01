namespace Identity.API.Models;

public class UserRestaurant
{
    public required Guid UserId { get; set; }
    public required ApplicationUser User { get; set; }
    // The tenant identifier switched from int to Guid so the JWT claim value
    // parses correctly in every consumer (Guid.TryParse("42") returns
    // false). The migration truncates UserRestaurants because old
    // integer IDs do not map to a valid UUID; production deploys
    // start on an empty table seed-gate change.
    public required Guid RestaurantId { get; set; }
    public bool IsDefault { get; set; }
}
