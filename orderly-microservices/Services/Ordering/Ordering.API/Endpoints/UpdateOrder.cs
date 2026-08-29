using Ordering.Application.Orders.Commands.UpdateOrder;

namespace Ordering.API.Endpoints;

public record UpdateOrderRequest(OrderDto Order);

public record UpdateOrderResponse(bool IsSuccess);

public class UpdateOrder : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Orders");

        group.MapPut("/orders", async (UpdateOrderRequest request, ISender sender) =>
        {
            UpdateOrderCommand? command = request.Adapt<UpdateOrderCommand>();
            UpdateOrderResult? result = await sender.Send(command);
            UpdateOrderResponse? response = result.Adapt<UpdateOrderResponse>();

            return Results.Ok(response);
        })
        .RequirePermission("orders:write")
        .WithDescription("Updates an existing order.")
        .WithName("UpdateOrder")
        .Produces<UpdateOrderResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
