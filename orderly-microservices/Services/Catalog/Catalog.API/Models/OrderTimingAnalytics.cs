namespace Catalog.API.Models;

public class OrderTimingAnalytics : Entity<int>
{
    public Guid OrderId { get; set; }
    public Guid RestaurantId { get; set; }
    public int OrderingToPendingSeconds { get; set; }
    public int PendingToConfirmedSeconds { get; set; }
    public int ConfirmedToPreparingSeconds { get; set; }
    public int PreparingToReadySeconds { get; set; }
    public int ReadyToDeliveredSeconds { get; set; }
    public int TotalTimeSeconds { get; set; }
    public int EstimatedPrepMinutes { get; set; }
    public int ActualPrepMinutes { get; set; }
    public int PrepTimeDifferenceMinutes { get; set; }
    public Guid? WaiterUserId { get; set; }
    public int KitchenStaffCount { get; set; }
    public Instant RecordedAt { get; set; }
}
