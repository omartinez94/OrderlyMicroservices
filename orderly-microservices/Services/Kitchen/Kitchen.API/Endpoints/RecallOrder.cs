namespace Kitchen.API.Endpoints;

/// <summary>
/// <c>POST /v1/kitchen/tickets/{id}/recall</c> — chef pull-back: a
/// <c>Bumped</c> ticket moves back to <c>Ready</c>.
/// </summary>
public class RecallOrder : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/kitchen/tickets/{id:guid}/recall", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new RecallOrderCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .WithTags("Kitchen")
        .RequirePermission("kitchen:update_prep_status");
    }
}