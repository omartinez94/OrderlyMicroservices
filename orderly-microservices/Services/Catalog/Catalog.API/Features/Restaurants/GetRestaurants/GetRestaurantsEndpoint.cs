namespace Catalog.API.Features.Restaurants.GetRestaurants;

public record GetRestaurantsRequest(int? PageNumber = 1, int? PageSize = 10);

public record GetRestaurantsResponse(IEnumerable<Restaurant> Restaurants);

public class GetRestaurantsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Restaurants");

        group.MapGet("/restaurants", async ([AsParameters] GetRestaurantsRequest request, ISender sender) =>
        {
            GetRestaurantsQuery? query = request.Adapt<GetRestaurantsQuery>();
            GetRestaurantsResult? result = await sender.Send(query);
            GetRestaurantsResponse? response = result.Adapt<GetRestaurantsResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a list of restaurants.")
        .WithName("GetRestaurants")
        .Produces<GetRestaurantsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
