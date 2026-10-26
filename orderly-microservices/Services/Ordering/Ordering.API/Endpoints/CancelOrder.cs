using Ordering.Application.Orders.Commands.CancelOrder;

namespace Ordering.API.Endpoints;

/// <summary>
/// <c>POST /v1/orders/{id}/cancel</c> — cancel an order. Body shape:
/// <c>{ "reason": "..." }</c>. Returns 204 on success; 403 when caller lacks
/// approve_cancel permission and order is past Pending; 404 when the
/// order is unknown; 409 when the order is already in a terminal state
/// (Cancelled, Completed, Delivered).
/// </summary>
public class CancelOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGroup("/v1").WithTags("Orders")
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
            .RequirePermission("orders:cancel")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    public record CancelOrderRequest(string Reason);
}
