namespace Catalog.API.Features.WalkInQueues.AddToWalkInQueue;

public record AddToWalkInQueueRequest(
    string CustomerName,
    string CustomerPhone,
    int PartySize,
    int EstimatedWaitMinutes);

public record AddToWalkInQueueResponse(int Id);

public class AddToWalkInQueueEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("WalkInQueues");

        group.MapPost("/restaurants/{restaurantId}/walk-in-queue", async (Guid restaurantId, AddToWalkInQueueRequest request, ISender sender) =>
        {
            var command = request.Adapt<AddToWalkInQueueCommand>() with { RestaurantId = restaurantId };
            var result = await sender.Send(command);
            var response = result.Adapt<AddToWalkInQueueResponse>();

            return Results.Created($"/api/v1/restaurants/{restaurantId}/walk-in-queue/{response.Id}", response);
        })
        .WithDescription("Adds a customer to the walk-in queue.")
        .WithName("AddToWalkInQueue")
        .Produces<AddToWalkInQueueResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
