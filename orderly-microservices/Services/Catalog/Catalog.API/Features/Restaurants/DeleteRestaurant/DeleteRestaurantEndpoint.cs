namespace Catalog.API.Features.Restaurants.DeleteRestaurant;

public record DeleteRestaurantResponse(bool IsSuccess);

public class DeleteRestaurantEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Restaurants");

        group.MapDelete("/restaurants/{id}", async (Guid id, ISender sender) =>
        {
            DeleteRestaurantCommand? command = new DeleteRestaurantCommand(id);
            DeleteRestaurantResult? result = await sender.Send(command);
            DeleteRestaurantResponse? response = result.Adapt<DeleteRestaurantResponse>();

            return Results.Ok(response);
        })
        .RequirePermission("catalog:menu_update")
        .WithDescription("Deletes a restaurant.")
        .WithName("DeleteRestaurant")
        .Produces<DeleteRestaurantResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
