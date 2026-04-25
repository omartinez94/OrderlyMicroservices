namespace Catalog.API.Models;

public class Reservation : AuditableEntity<Guid>
{
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int PartySize { get; set; }
    public bool ReminderSent { get; set; }
    public bool RequiresApproval { get; set; }
    public LocalDate ReservationDate { get; set; }
    public string ReservationNumber { get; set; } = string.Empty;
    public LocalTime ReservationTime { get; set; }
    public Guid RestaurantId { get; set; }
    public string SpecialRequests { get; set; } = string.Empty;
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
    public Instant? ApprovedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public Instant? CancelledAt { get; set; }
    public Instant? CompletedAt { get; set; }
    public Instant? ConfirmedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Instant? ReminderSentAt { get; set; }
    public Instant? SeatedAt { get; set; }
    public Guid? TableId { get; set; }
}
