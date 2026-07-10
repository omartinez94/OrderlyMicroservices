using Ordering.Application.Orders.Commands.MarkOrderReady;

namespace Ordering.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/orders/{id}/mark-ready</c> — drive an order from
/// <c>Preparing</c> to <c>Ready</c>. Returns 204 on success; 404 when
/// the order is unknown; 409 on illegal transition.
/// </summary>
public class MarkOrderReady : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1").WithTags("Kitchen")
            .MapPost("/orders/{id:guid}/mark-ready", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                await sender.Send(new MarkOrderReadyCommand(id), cancellationToken);
                return Results.NoContent();
            })
            .RequirePermission("kitchen:update_prep_status")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}