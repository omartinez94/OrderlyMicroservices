namespace Catalog.API.Models;

public class NotificationLog : Entity<int>
{
    /// <summary>Delivery method: email, whatsapp, sms</summary>
    public NotificationChannel Channel { get; set; }
    public Instant CreatedAt { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public string MessageContent { get; set; } = string.Empty;
    /// <summary>Type of notification: order_confirmation, feedback_request, etc.</summary>
    public string MessageType { get; set; } = string.Empty;
    public string RecipientIdentifier { get; set; } = string.Empty;
    /// <summary>Target audience: customer, staff, manager</summary>
    public RecipientType RecipientType { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public Guid? RelatedOrderId { get; set; }
    public Guid? RelatedReservationId { get; set; }
    public Instant? SentAt { get; set; }
}
