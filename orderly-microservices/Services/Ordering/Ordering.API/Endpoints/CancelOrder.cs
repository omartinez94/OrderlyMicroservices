using Ordering.Application.Orders.Commands.CancelOrder;

namespace Ordering.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/orders/{id}/cancel</c> — cancel an order. Body shape:
/// <c>{ "reason": "..." }</c>. Returns 204 on success; 404 when the
/// order is unknown; 409 when the order is already in a terminal state
/// (Cancelled, Completed, Delivered).
/// </summary>
public class CancelOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1").WithTags("Kitchen")
            .MapPost("/orders/{id:guid}/cancel", async (
                Guid id,
                CancelOrderRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                await sender.Send(
                    new CancelOrderCommand(id, request.Reason),
                    cancellationToken);
                return Results.NoContent();
            })
            .RequirePermission("kitchen:update_prep_status")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    public record CancelOrderRequest(string Reason);
}