using Ordering.Application.Orders.Commands.CreateOrder;

namespace Ordering.API.Endpoints;

public record CreateOrderRequest(OrderDto Order);

public record CreateOrderResponse(Guid Id);

public class CreateOrder : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Orders");

        group.MapPost("/orders", async (CreateOrderRequest request, ISender sender) =>
        {
            CreateOrderCommand? command = request.Adapt<CreateOrderCommand>();
            CreateOrderResult? result = await sender.Send(command);
            CreateOrderResponse? response = result.Adapt<CreateOrderResponse>();

            return Results.Created($"/v1/orders/{response.Id}", response);
        })
        .RequirePermission("orders:write")
        .WithDescription("Creates a new order.")
        .WithName("CreateOrder")
        .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
