namespace Catalog.API.Features.Restaurants.GetRestaurantById;

public record GetRestaurantByIdResponse(Restaurant Restaurant);

public class GetRestaurantByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Restaurants");

        group.MapGet("/restaurants/{id}", async (string id, ISender sender) =>
        {
            GetRestaurantByIdQuery? query = new GetRestaurantByIdQuery(id);
            GetRestaurantByIdResult? result = await sender.Send(query);
            GetRestaurantByIdResponse? response = result.Adapt<GetRestaurantByIdResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a restaurant by id or short id.")
        .WithName("GetRestaurantById")
        .Produces<GetRestaurantByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
