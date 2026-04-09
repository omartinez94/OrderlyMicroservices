using NodaTime;

namespace Basket.API.Models;

public class Basket
{
    public Basket()
    {
    }

    public Basket(Guid userId, Guid restaurantId)
    {
        UserId = userId;
        RestaurantId = restaurantId;
    }

    public Guid UserId { get; set; }
    public Guid RestaurantId { get; set; }
    public List<BasketItem> Items { get; set; } = new();
    public List<string> AppliedDiscounts { get; set; } = new();
    
    public decimal Subtotal => Items.Sum(x => x.TotalPrice);
    
    public Instant CreatedAt { get; set; }
    public Instant ExpiresAt { get; set; }
}
