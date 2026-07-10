using Ordering.Application.Orders.Commands.ConfirmOrder;

namespace Ordering.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/orders/{id}/confirm</c> — drive an order from
/// <c>Pending</c> to <c>Confirmed</c>. Returns 204 on success; 404 when
/// the order is unknown; 409 on illegal transition (e.g. already
/// <c>Confirmed</c>).
/// </summary>
public class ConfirmOrder : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1").WithTags("Kitchen")
            .MapPost("/orders/{id:guid}/confirm", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                await sender.Send(new ConfirmOrderCommand(id), cancellationToken);
                return Results.NoContent();
            })
            .RequirePermission("kitchen:update_prep_status")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}