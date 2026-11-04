using Kitchen.API.Application.KitchenTickets.Queries;

namespace Kitchen.API.Endpoints;

public class GetKitchenVelocity : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/kitchen/restaurants/{restaurantId:guid}/velocity", async (
            Guid restaurantId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            KitchenVelocityResultDto result = await sender.Send(new GetKitchenVelocityQuery(restaurantId), cancellationToken);
            return Results.Ok(result);
        })
        .WithTags("Kitchen")
        .RequirePermission("dashboard:view");
    }
}
