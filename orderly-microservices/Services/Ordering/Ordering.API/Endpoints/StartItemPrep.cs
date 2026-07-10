using Ordering.Application.Orders.Commands.StartItemPrep;

namespace Ordering.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/orders/{id}/items/{itemId}/start-prep</c> — mark a
/// single line item as <c>Preparing</c>. Returns 204 on success; 404
/// when the order or item is unknown; 409 on illegal item-state
/// transition.
/// </summary>
public class StartItemPrep : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1").WithTags("Kitchen")
            .MapPost("/orders/{id:guid}/items/{itemId:guid}/start-prep", async (
                Guid id,
                Guid itemId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                await sender.Send(
                    new StartItemPrepCommand(id, itemId),
                    cancellationToken);
                return Results.NoContent();
            })
            .RequirePermission("kitchen:update_prep_status")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}