namespace Catalog.API.Models;

public class Reservation : AuditableEntity<Guid>
{
    public Guid RestaurantId { get; set; }
    public string ReservationNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public int PartySize { get; set; }
    public LocalDate ReservationDate { get; set; }
    public LocalTime ReservationTime { get; set; }
    public Guid? TableId { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
    public bool RequiresApproval { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public Instant? ApprovedAt { get; set; }
    public string SpecialRequests { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool ReminderSent { get; set; }
    public Instant? ReminderSentAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Instant? ConfirmedAt { get; set; }
    public Instant? SeatedAt { get; set; }
    public Instant? CompletedAt { get; set; }
    public Instant? CancelledAt { get; set; }
}
