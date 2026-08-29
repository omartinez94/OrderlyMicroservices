using Ordering.Application.Orders.Queries.GetOrderById;

namespace Ordering.API.Endpoints;

public record GetOrderByIdResponse(OrderDto Order);

public class GetOrderById : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Orders");

        group.MapGet("/orders/{id}", async (Guid id, ISender sender) =>
        {
            GetOrderByIdQuery? query = new GetOrderByIdQuery(id);
            GetOrderByIdResult? result = await sender.Send(query);
            GetOrderByIdResponse? response = result.Adapt<GetOrderByIdResponse>();

            return Results.Ok(response);
        })
        .RequirePermission("orders:view_own")
        .WithDescription("Gets an order by Id.")
        .WithName("GetOrderById")
        .Produces<GetOrderByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
