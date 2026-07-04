namespace Kitchen.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/kitchen/tickets/{id}/mark-ready</c> — move the whole
/// ticket to <c>Ready</c>. Permitted only when every item is in
/// <c>KitchenItemStatus.Ready</c>; otherwise 409.
/// </summary>
public class MarkOrderReady : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/kitchen/tickets/{id:guid}/mark-ready", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            MarkOrderReadyResult result = await sender.Send(new MarkOrderReadyCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .WithTags("Kitchen")
        .RequireAuthorization("kitchen:update_prep_status");
    }
}