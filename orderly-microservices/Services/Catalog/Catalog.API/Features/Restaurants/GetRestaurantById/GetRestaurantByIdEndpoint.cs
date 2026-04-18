namespace Catalog.API.Features.Restaurants.GetRestaurantById;

public record GetRestaurantByIdResponse(Restaurant Restaurant);

public class GetRestaurantByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/restaurants/{id}", async (Guid id, ISender sender) =>
        {
            var query = new GetRestaurantByIdQuery(id);
            var result = await sender.Send(query);
            var response = result.Adapt<GetRestaurantByIdResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a restaurant by id.")
        .WithName("GetRestaurantById")
        .Produces<GetRestaurantByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
