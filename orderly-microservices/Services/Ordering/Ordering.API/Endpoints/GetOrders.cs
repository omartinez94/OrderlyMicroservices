using Ordering.Application.Orders.Queries.GetOrders;

namespace Ordering.API.Endpoints;

public record GetOrdersResponse(PaginatedResult<OrderDto> Orders);

public class GetOrders : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Orders");

        group.MapGet("/orders", async ([AsParameters] PaginationRequest request, ISender sender) =>
        {
            var query = new GetOrdersQuery(request);
            var result = await sender.Send(query);
            var response = result.Adapt<GetOrdersResponse>();

            return Results.Ok(response);
        })
        .RequirePermission("orders:view_own")
        .WithDescription("Gets a paginated list of orders.")
        .WithName("GetOrders")
        .Produces<GetOrdersResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
