using Ordering.Application.Orders.Commands.StartOrderPrep;

namespace Ordering.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/orders/{id}/start-prep</c> — drive an order from
/// <c>Confirmed</c> to <c>Preparing</c>. Returns 204 on success; 404 when
/// the order is unknown; 409 on illegal transition.
/// </summary>
public class StartOrderPrep : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1").WithTags("Kitchen")
            .MapPost("/orders/{id:guid}/start-prep", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                await sender.Send(new StartOrderPrepCommand(id), cancellationToken);
                return Results.NoContent();
            })
            .RequirePermission("kitchen:update_prep_status")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}