using Ordering.Application.Orders.Commands.DeleteOrder;

namespace Ordering.API.Endpoints;

// Request could just be the Id in the route, but usually we define a record for documentation
public record DeleteOrderResponse(bool IsSuccess);

public class DeleteOrder : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Orders");

        group.MapDelete("/orders/{id}", async (Guid id, ISender sender) =>
        {
            DeleteOrderCommand? command = new DeleteOrderCommand(id);
            DeleteOrderResult? result = await sender.Send(command);
            DeleteOrderResponse? response = result.Adapt<DeleteOrderResponse>();

            return Results.Ok(response);
        })
        .RequirePermission("orders:write")
        .WithDescription("Deletes an order.")
        .WithName("DeleteOrder")
        .Produces<DeleteOrderResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
