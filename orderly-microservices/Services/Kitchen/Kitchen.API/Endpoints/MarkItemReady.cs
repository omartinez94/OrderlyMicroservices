namespace Kitchen.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/kitchen/tickets/{id}/items/{itemId}/ready</c> — mark a
/// single item as <c>Ready</c>. Returns 204 on success; 404 when the ticket
/// is unknown; 409 on illegal transition (item not yet <c>Preparing</c> or
/// ticket already <c>Ready</c>/<c>Bumped</c>/<c>Cancelled</c>).
/// </summary>
public class MarkItemReady : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/kitchen/tickets/{id:guid}/items/{itemId:guid}/ready", async (
            Guid id,
            Guid itemId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new MarkItemReadyCommand(id, itemId), cancellationToken);
            return Results.NoContent();
        })
        .WithTags("Kitchen")
        .RequireAuthorization("kitchen:update_prep_status");
    }
}