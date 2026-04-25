namespace Catalog.API.Models;

public class WalkInQueue : AuditableEntity<int>
{
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int EstimatedWaitMinutes { get; set; }
    public int PartySize { get; set; }
    public Guid RestaurantId { get; set; }
    /// <summary>Status of the walk-in party: waiting, notified, seated, cancelled, no_show</summary>
    public WalkInQueueStatus Status { get; set; } = WalkInQueueStatus.Waiting;
    public Instant? NotifiedAt { get; set; }
    public Instant? SeatedAt { get; set; }
    public Guid? TableId { get; set; }
}
