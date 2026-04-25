namespace Catalog.API.Models;

public class CustomerFeedback : Entity<int>
{
    public Guid OrderId { get; set; }
    public Guid RestaurantId { get; set; }
    public int FoodQualityRating { get; set; }
    public int ServiceSpeedRating { get; set; }
    public int WaiterFriendlinessRating { get; set; }
    public int OverallRating { get; set; }
    public string Comments { get; set; } = string.Empty;
    public string RewardCode { get; set; } = string.Empty;
    /// <summary>Type of reward issued: percentage, free_item, points</summary>
    public string RewardType { get; set; } = string.Empty;
    public decimal? RewardValue { get; set; }
    public string RewardDescription { get; set; } = string.Empty;
    public bool RewardRedeemed { get; set; }
    public Instant? RedeemedAt { get; set; }
    public Guid? RedeemedInOrderId { get; set; }
    public Instant SubmittedAt { get; set; }
}
