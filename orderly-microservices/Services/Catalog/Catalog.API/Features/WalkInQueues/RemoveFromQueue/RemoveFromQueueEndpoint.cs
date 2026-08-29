namespace Catalog.API.Features.WalkInQueues.RemoveFromQueue;

public record RemoveFromQueueResponse(bool IsSuccess);

public class RemoveFromQueueEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("WalkInQueues");

        group.MapDelete("/restaurants/{restaurantId}/walk-in-queue/{id}", async (int id, ISender sender) =>
        {
            RemoveFromQueueCommand? command = new RemoveFromQueueCommand(id);
            RemoveFromQueueResult? result = await sender.Send(command);
            RemoveFromQueueResponse? response = result.Adapt<RemoveFromQueueResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Removes a customer from the walk-in queue (cancelled).")
        .WithName("RemoveFromQueue")
        .Produces<RemoveFromQueueResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
