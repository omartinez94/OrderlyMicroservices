namespace Catalog.API.Models;

public class CustomerFeedback : Entity<int>
{
    public string Comments { get; set; } = string.Empty;
    public int FoodQualityRating { get; set; }
    public Guid OrderId { get; set; }
    public int OverallRating { get; set; }
    public Guid RestaurantId { get; set; }
    public string RewardCode { get; set; } = string.Empty;
    public string RewardDescription { get; set; } = string.Empty;
    public bool RewardRedeemed { get; set; }
    /// <summary>Type of reward issued: percentage, free_item, points</summary>
    public string RewardType { get; set; } = string.Empty;
    public int ServiceSpeedRating { get; set; }
    public Instant SubmittedAt { get; set; }
    public int WaiterFriendlinessRating { get; set; }
    public Instant? RedeemedAt { get; set; }
    public Guid? RedeemedInOrderId { get; set; }
    public decimal? RewardValue { get; set; }
}
