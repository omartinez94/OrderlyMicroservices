using Ordering.Application.Orders.Commands.RequestCancelOrder;

namespace Ordering.API.Endpoints;

/// <summary>
/// <c>POST /v1/orders/{id}/cancel-request</c> — request cancellation for an order.
/// Permitted from Confirmed, Preparing, or Ready status. Transitions status to CancelRequested.
/// </summary>
public class RequestCancelOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1").WithTags("Orders");

        // Map both cancel-request and request-cancel for full spec compatibility
        RouteHandlerBuilder cancelRequestHandler = group.MapPost("/orders/{id:guid}/cancel-request", HandleRequest);
        RouteHandlerBuilder requestCancelHandler = group.MapPost("/orders/{id:guid}/request-cancel", HandleRequest);

        ConfigureEndpoint(cancelRequestHandler);
        ConfigureEndpoint(requestCancelHandler);
    }

    private static async Task<IResult> HandleRequest(
        Guid id,
        RequestCancelOrderRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new RequestCancelOrderCommand(id, request.Reason),
            cancellationToken);
        return Results.NoContent();
    }

    private static void ConfigureEndpoint(RouteHandlerBuilder builder)
    {
        builder
            .RequirePermission("orders:request_cancel")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    public record RequestCancelOrderRequest(string Reason);
}
