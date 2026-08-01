using Ordering.Application.Orders.Commands.CreateOrder;

namespace Ordering.API.Endpoints;

public record CreateOrderRequest(OrderDto Order);

public record CreateOrderResponse(Guid Id);

public class CreateOrder : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Orders");

        group.MapPost("/orders", async (CreateOrderRequest request, ISender sender) =>
        {
            var command = request.Adapt<CreateOrderCommand>();
            var result = await sender.Send(command);
            var response = result.Adapt<CreateOrderResponse>();

            return Results.Created($"/api/v1/orders/{response.Id}", response);
        })
        .RequirePermission("orders:write")
        .WithDescription("Creates a new order.")
        .WithName("CreateOrder")
        .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
