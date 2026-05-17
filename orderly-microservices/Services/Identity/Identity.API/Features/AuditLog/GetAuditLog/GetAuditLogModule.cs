namespace Identity.API.Features.AuditLog.GetAuditLog;

public class GetAuditLogModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit-log")
            .RequireAuthorization();

        group.MapGet("", async (
                [AsParameters] GetAuditLogRequest request,
                ISender sender,
                CancellationToken ct) =>
            {
                var query = new GetAuditLogQuery(request.Page, request.PageSize, request.UserId, request.EventType);
                var response = await sender.Send(query, ct);

                return Results.Ok(response);
            })
            .Produces<GetAuditLogResponse>(200);
    }
}

public record GetAuditLogRequest(int Page = 1, int PageSize = 50, Guid? UserId = null, string? EventType = null);
