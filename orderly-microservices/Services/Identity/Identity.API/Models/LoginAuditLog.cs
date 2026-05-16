namespace Identity.API.Models;

public class LoginAuditLog
{
    public required Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public required string EventType { get; set; }
    public required string IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public required DateTimeOffset Timestamp { get; set; }
    public string? Details { get; set; }
}
