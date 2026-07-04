namespace Kitchen.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/kitchen/tickets/{id}/bump</c> — expo pass: move a
/// <c>Ready</c> ticket to <c>Bumped</c>.
/// </summary>
public class BumpOrder : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/kitchen/tickets/{id:guid}/bump", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new BumpOrderCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .WithTags("Kitchen")
        .RequirePermission("kitchen:update_prep_status");
    }
}