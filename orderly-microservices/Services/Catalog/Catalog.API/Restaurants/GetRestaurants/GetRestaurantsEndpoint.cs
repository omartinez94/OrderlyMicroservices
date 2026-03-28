namespace Catalog.API.Restaurants.GetRestaurants;

public record GetRestaurantsRequest(int? PageNumber = 1, int? PageSize = 10);

public record GetRestaurantsResponse(IEnumerable<Restaurant> Restaurants);

internal class GetRestaurantsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/restaurants", async ([AsParameters] GetRestaurantsRequest request, ISender sender) =>
        {
            var query = request.Adapt<GetRestaurantsQuery>();
            var result = await sender.Send(query);
            var response = result.Adapt<GetRestaurantsResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a list of restaurants.")
        .WithName("GetRestaurants")
        .Produces<GetRestaurantsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
