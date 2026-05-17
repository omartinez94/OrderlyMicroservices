namespace Identity.API.Services;

public class AuditLogger(IdentityDbContext dbContext)
{
    public async Task LogAsync(
        Guid? userId,
        string eventType,
        string ipAddress,
        string userAgent,
        string? details = null,
        CancellationToken ct = default)
    {
        var log = new LoginAuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventType = eventType,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Timestamp = DateTimeOffset.UtcNow,
            Details = details
        };

        dbContext.LoginAuditLogs.Add(log);
        await dbContext.SaveChangesAsync(ct);
    }
}
