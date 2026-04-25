namespace Catalog.API.Models;

public class WalkInQueue : AuditableEntity<int>
{
    public Guid RestaurantId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int PartySize { get; set; }
    /// <summary>Status of the walk-in party: waiting, notified, seated, cancelled, no_show</summary>
    public WalkInQueueStatus Status { get; set; } = WalkInQueueStatus.Waiting;
    public int EstimatedWaitMinutes { get; set; }
    public Instant? NotifiedAt { get; set; }
    public Instant? SeatedAt { get; set; }
    public Guid? TableId { get; set; }
}
