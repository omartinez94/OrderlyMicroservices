namespace Identity.API.Models;

public class LoginAuditLog
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string? Details { get; set; }
}
