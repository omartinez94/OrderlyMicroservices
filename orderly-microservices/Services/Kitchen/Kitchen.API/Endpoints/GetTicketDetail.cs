namespace Kitchen.API.Endpoints;

/// <summary>
/// <c>GET /api/v1/kitchen/tickets/{id}</c> — single ticket with items.
/// Returns 404 via <c>KitchenTicketNotFoundException</c> when the id is unknown.
/// </summary>
public class GetTicketDetail : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/kitchen/tickets/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            KitchenTicketDto ticket = await sender.Send(new GetTicketByIdQuery(id), cancellationToken);
            return Results.Ok(ticket);
        })
        .WithTags("Kitchen")
        .RequirePermission("kitchen:view_orders");
    }
}