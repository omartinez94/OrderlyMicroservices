namespace Kitchen.API.Endpoints;

/// <summary>
/// <c>POST /v1/kitchen/tickets/{id}/reject-cancel</c> — body
/// <c>{ "reason": "..." }</c> (optional). Rejects an active cancellation request.
/// </summary>
public class RejectCancelRequest : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/kitchen/tickets/{id:guid}/reject-cancel", async (
            Guid id,
            RejectCancelRequestDto? body,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new RejectCancelRequestCommand(id, body?.Reason), cancellationToken);
            return Results.NoContent();
        })
        .WithTags("Kitchen")
        .RequirePermission("kitchen:update_prep_status");
    }

    private sealed record RejectCancelRequestDto(string? Reason);
}
