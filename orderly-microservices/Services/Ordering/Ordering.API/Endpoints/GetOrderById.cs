using Ordering.Application.Orders.Queries.GetOrderById;

namespace Ordering.API.Endpoints;

public record GetOrderByIdResponse(OrderDto Order);

public class GetOrderById : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Orders");

        group.MapGet("/orders/{id}", async (Guid id, ISender sender) =>
        {
            var query = new GetOrderByIdQuery(id);
            var result = await sender.Send(query);
            var response = result.Adapt<GetOrderByIdResponse>();

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
