namespace Identity.API.Features.Users.AssignRestaurants;

public class AssignRestaurantsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        group.MapPut("{id:guid}/restaurants", async (
                Guid id,
                ISender sender,
                AssignRestaurantsRequest request,
                CancellationToken ct) =>
            {
                var command = new AssignRestaurantsCommand(id, request.Restaurants);
                var response = await sender.Send(command, ct);

                return Results.Ok(response);
            }).RequirePermission("users:assign_restaurants").Accepts<AssignRestaurantsRequest>("application/json")
            .Produces<AssignRestaurantsResponse>(200)
            .ProducesProblem(404);
    }
}

public record AssignRestaurantsRequest(List<AssignRestaurants.RestaurantAssignment> Restaurants);

