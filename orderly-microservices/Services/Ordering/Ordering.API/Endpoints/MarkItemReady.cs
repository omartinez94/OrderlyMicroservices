using Ordering.Application.Orders.Commands.MarkItemReady;

namespace Ordering.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/orders/{id}/items/{itemId}/mark-ready</c> — mark a
/// single line item as <c>Ready</c>. Returns 204 on success; 404 when
/// the order or item is unknown; 409 on illegal item-state transition
/// (item not yet <c>Preparing</c> or already <c>Ready</c>).
/// </summary>
public class MarkItemReady : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1").WithTags("Kitchen")
            .MapPost("/orders/{id:guid}/items/{itemId:guid}/mark-ready", async (
                Guid id,
                Guid itemId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                await sender.Send(
                    new MarkItemReadyCommand(id, itemId),
                    cancellationToken);
                return Results.NoContent();
            })
            .RequirePermission("kitchen:update_prep_status")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}