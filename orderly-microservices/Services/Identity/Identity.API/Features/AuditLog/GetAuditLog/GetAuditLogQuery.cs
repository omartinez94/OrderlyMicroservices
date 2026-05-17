namespace Identity.API.Features.AuditLog.GetAuditLog;

public record GetAuditLogQuery(int Page = 1, int PageSize = 50, Guid? UserId = null, string? EventType = null) : IQuery<GetAuditLogResponse>;

public record GetAuditLogResponse(IEnumerable<AuditLogEntry> Logs, int TotalCount);

public record AuditLogEntry(
    Guid Id,
    Guid? UserId,
    string? UserName,
    string EventType,
    string IpAddress,
    string UserAgent,
    DateTimeOffset Timestamp,
    string? Details);

public class GetAuditLogQueryHandler(IdentityDbContext dbContext)
    : IQueryHandler<GetAuditLogQuery, GetAuditLogResponse>
{
    public async Task<GetAuditLogResponse> Handle(GetAuditLogQuery query, CancellationToken cancellationToken)
    {
        var logsQuery = dbContext.LoginAuditLogs.AsQueryable();

        if (query.UserId.HasValue)
        {
            logsQuery = logsQuery.Where(l => l.UserId == query.UserId);
        }

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            logsQuery = logsQuery.Where(l => l.EventType == query.EventType);
        }

        var totalCount = await logsQuery.CountAsync(cancellationToken);

        var logs = await logsQuery
            .OrderByDescending(l => l.Timestamp)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(l => new AuditLogEntry(
                l.Id,
                l.UserId,
                l.UserId.HasValue ? dbContext.Users.Where(u => u.Id == l.UserId).Select(u => u.UserName).FirstOrDefault() : null,
                l.EventType,
                l.IpAddress,
                l.UserAgent,
                l.Timestamp,
                l.Details))
            .ToListAsync(cancellationToken);

        return new GetAuditLogResponse(logs, totalCount);
    }
}
