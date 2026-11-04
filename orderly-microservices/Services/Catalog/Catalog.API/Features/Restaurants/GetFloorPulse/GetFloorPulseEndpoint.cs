namespace Catalog.API.Features.Restaurants.GetFloorPulse;

public record GetFloorPulseResponse(
    FloorPulseData FloorPulse,
    IReadOnlyList<WaitlistData> Waitlist,
    string Currency);

public class GetFloorPulseEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1").WithTags("Restaurants");

        group.MapGet("/restaurants/{restaurantId:guid}/floor-pulse", async (Guid restaurantId, ISender sender, CancellationToken ct) =>
        {
            GetFloorPulseQuery query = new(restaurantId);
            GetFloorPulseResult result = await sender.Send(query, ct);
            GetFloorPulseResponse response = new(result.FloorPulse, result.Waitlist, result.Currency);
            return Results.Ok(response);
        })
        .RequirePermission("dashboard:view")
        .WithDescription("Gets floor pulse and waitlist for a restaurant dashboard.")
        .WithName("GetFloorPulse")
        .Produces<GetFloorPulseResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
