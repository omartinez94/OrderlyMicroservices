namespace Catalog.API.Features.Restaurants.DeleteRestaurant;

public record DeleteRestaurantResponse(bool IsSuccess);

public class DeleteRestaurantEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("/restaurants/{id}", async (Guid id, ISender sender) =>
        {
            var command = new DeleteRestaurantCommand(id);
            var result = await sender.Send(command);
            var response = result.Adapt<DeleteRestaurantResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Deletes a restaurant.")
        .WithName("DeleteRestaurant")
        .Produces<DeleteRestaurantResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
