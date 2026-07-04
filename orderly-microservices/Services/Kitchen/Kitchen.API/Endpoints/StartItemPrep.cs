namespace Kitchen.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/kitchen/tickets/{id}/items/{itemId}/start</c> — mark a
/// single item as <c>Preparing</c>. Returns 204 on success; 404 when the
/// ticket is unknown; 409 on illegal transition.
/// </summary>
public class StartItemPrep : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/kitchen/tickets/{id:guid}/items/{itemId:guid}/start", async (
            Guid id,
            Guid itemId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new StartItemPrepCommand(id, itemId), cancellationToken);
            return Results.NoContent();
        })
        .WithTags("Kitchen")
        .RequirePermission("kitchen:update_prep_status");
    }
}