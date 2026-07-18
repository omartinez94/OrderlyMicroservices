using Marten.Schema;

namespace Basket.API.Models;

/// <summary>
/// The cart document. Implements <see cref="ITenantEntity"/> so the
/// Marten <c>MultiTenanted()</c> registration in <c>Program.cs</c> tags
/// every row with the current restaurant id; per-request reads/writes
/// are filtered by <see cref="ICurrentRestaurantProvider"/> so a
/// caller cannot reach across tenants.
/// </summary>
public class Basket : ITenantEntity
{
    public Basket()
    {
    }

    public Basket(Guid userId, Guid restaurantId)
    {
        UserId = userId;
        RestaurantId = restaurantId;
    }

    [Identity]
    public Guid UserId { get; set; }
    public Guid RestaurantId { get; set; }
    public List<BasketItem> Items { get; set; } = [];
    public List<string> AppliedDiscounts { get; set; } = [];
    
    public decimal Subtotal => Items.Sum(x => x.TotalPrice);
    
    public Instant CreatedAt { get; set; }
    public Instant ExpiresAt { get; set; }
}
