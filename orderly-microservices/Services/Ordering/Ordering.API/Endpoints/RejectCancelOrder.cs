using Ordering.Application.Orders.Commands.RejectCancelOrder;

namespace Ordering.API.Endpoints;

/// <summary>
/// <c>POST /v1/orders/{id}/cancel-reject</c> — reject cancellation for an order in CancelRequested status.
/// Restores the order to its previous status.
/// </summary>
public class RejectCancelOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1").WithTags("Orders");

        RouteHandlerBuilder cancelRejectHandler = group.MapPost("/orders/{id:guid}/cancel-reject", HandleRequest);
        RouteHandlerBuilder rejectCancelHandler = group.MapPost("/orders/{id:guid}/reject-cancel", HandleRequest);

        ConfigureEndpoint(cancelRejectHandler);
        ConfigureEndpoint(rejectCancelHandler);
    }

    private static async Task<IResult> HandleRequest(
        Guid id,
        RejectCancelOrderRequest? request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new RejectCancelOrderCommand(id, request?.Reason),
            cancellationToken);
        return Results.NoContent();
    }

    private static void ConfigureEndpoint(RouteHandlerBuilder builder)
    {
        builder
            .RequirePermission("orders:approve_cancel")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    public record RejectCancelOrderRequest(string? Reason);
}
