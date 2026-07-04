namespace Kitchen.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/kitchen/tickets/{id}/cancel</c> — body
/// <c>{ "reason": "..." }</c>. Cancels the ticket from any non-terminal
/// state. Returns 400 when the reason is empty or longer than 500 chars;
/// 404 when the ticket is unknown; 409 if already <c>Cancelled</c>.
/// </summary>
public class CancelOrder : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/kitchen/tickets/{id:guid}/cancel", async (
            Guid id,
            CancelOrderRequest body,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new CancelOrderCommand(id, body.Reason), cancellationToken);
            return Results.NoContent();
        })
        .WithTags("Kitchen")
        .RequireAuthorization("kitchen:update_prep_status");
    }

    private record CancelOrderRequest(string Reason);
}