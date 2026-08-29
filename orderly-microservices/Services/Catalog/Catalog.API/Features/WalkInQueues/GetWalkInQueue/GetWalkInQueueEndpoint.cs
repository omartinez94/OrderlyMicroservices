namespace Catalog.API.Features.WalkInQueues.GetWalkInQueue;

public record GetWalkInQueueRequest(Guid RestaurantId, WalkInQueueStatus? Status = null);

public record GetWalkInQueueResponse(IEnumerable<WalkInQueue> Entries);

public class GetWalkInQueueEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("WalkInQueues");

        group.MapGet("/restaurants/{restaurantId}/walk-in-queue", async ([AsParameters] GetWalkInQueueRequest request, ISender sender) =>
        {
            GetWalkInQueueQuery? query = request.Adapt<GetWalkInQueueQuery>();
            GetWalkInQueueResult? result = await sender.Send(query);
            GetWalkInQueueResponse? response = result.Adapt<GetWalkInQueueResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets the walk-in queue for a restaurant, optionally filtered by status.")
        .WithName("GetWalkInQueue")
        .Produces<GetWalkInQueueResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
