namespace Catalog.API.Features.WalkInQueues.GetWalkInQueue;

public record GetWalkInQueueRequest(Guid RestaurantId, WalkInQueueStatus? Status = null);

public record GetWalkInQueueResponse(IEnumerable<WalkInQueue> Entries);

public class GetWalkInQueueEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("WalkInQueues");

        group.MapGet("/restaurants/{restaurantId}/walk-in-queue", async ([AsParameters] GetWalkInQueueRequest request, ISender sender) =>
        {
            var query = request.Adapt<GetWalkInQueueQuery>();
            var result = await sender.Send(query);
            var response = result.Adapt<GetWalkInQueueResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets the walk-in queue for a restaurant, optionally filtered by status.")
        .WithName("GetWalkInQueue")
        .Produces<GetWalkInQueueResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
