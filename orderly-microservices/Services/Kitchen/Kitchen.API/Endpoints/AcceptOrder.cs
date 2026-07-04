namespace Kitchen.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/kitchen/tickets/{id}/accept</c> — accept a <c>New</c>
/// ticket. Returns 204 on success; 404 when the ticket is unknown; 409 on
/// illegal transition (e.g. ticket already <c>InProgress</c>).
/// </summary>
public class AcceptOrder : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/kitchen/tickets/{id:guid}/accept", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            AcceptOrderResult result = await sender.Send(new AcceptOrderCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .WithTags("Kitchen")
        .RequireAuthorization("kitchen:update_prep_status");
    }
}