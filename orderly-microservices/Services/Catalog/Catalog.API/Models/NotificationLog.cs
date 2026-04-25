namespace Catalog.API.Models;

public class NotificationLog : Entity<int>
{
    /// <summary>Target audience: customer, staff, manager</summary>
    public RecipientType RecipientType { get; set; }
    public string RecipientIdentifier { get; set; } = string.Empty;
    /// <summary>Delivery method: email, whatsapp, sms</summary>
    public NotificationChannel Channel { get; set; }
    
    /// <summary>Type of notification: order_confirmation, feedback_request, etc.</summary>
    public string MessageType { get; set; } = string.Empty;
    public Guid? RelatedOrderId { get; set; }
    public Guid? RelatedReservationId { get; set; }
    public string MessageContent { get; set; } = string.Empty;
    public NotificationStatus Status { get; set; }
    public Instant? SentAt { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public Instant CreatedAt { get; set; }
}
