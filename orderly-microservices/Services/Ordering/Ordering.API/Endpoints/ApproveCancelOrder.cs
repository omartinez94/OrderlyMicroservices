using Ordering.Application.Orders.Commands.ApproveCancelOrder;

namespace Ordering.API.Endpoints;

/// <summary>
/// <c>POST /v1/orders/{id}/cancel-approve</c> — approve cancellation for an order in CancelRequested status.
/// Transitions status to Cancelled.
/// </summary>
public class ApproveCancelOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1").WithTags("Orders");

        RouteHandlerBuilder cancelApproveHandler = group.MapPost("/orders/{id:guid}/cancel-approve", HandleRequest);
        RouteHandlerBuilder approveCancelHandler = group.MapPost("/orders/{id:guid}/approve-cancel", HandleRequest);

        ConfigureEndpoint(cancelApproveHandler);
        ConfigureEndpoint(approveCancelHandler);
    }

    private static async Task<IResult> HandleRequest(
        Guid id,
        ApproveCancelOrderRequest? request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new ApproveCancelOrderCommand(id, request?.Reason),
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

    public record ApproveCancelOrderRequest(string? Reason);
}
