namespace Identity.API.Features.Users.AssignRestaurants;

public class AssignRestaurantsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1/users");

        group.MapPut("{id:guid}/restaurants", async (
                Guid id,
                ISender sender,
                AssignRestaurantsRequest request,
                CancellationToken ct) =>
            {
                AssignRestaurantsCommand? command = new AssignRestaurantsCommand(id, request.Restaurants);
                AssignRestaurantsResponse? response = await sender.Send(command, ct);

                return Results.Ok(response);
            }).RequirePermission("users:assign_restaurants").Accepts<AssignRestaurantsRequest>("application/json")
            .Produces<AssignRestaurantsResponse>(200)
            .ProducesProblem(404);
    }
}

public record AssignRestaurantsRequest(List<AssignRestaurants.RestaurantAssignment> Restaurants);

