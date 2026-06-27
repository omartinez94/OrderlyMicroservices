namespace Catalog.API.Features.WalkInQueues.NotifyWalkInCustomer;

public record NotifyWalkInCustomerResponse(bool IsSuccess);

public class NotifyWalkInCustomerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("WalkInQueues");

        group.MapPut("/restaurants/{restaurantId}/walk-in-queue/{id}/notify", async (int id, ISender sender) =>
        {
            var command = new NotifyWalkInCustomerCommand(id);
            var result = await sender.Send(command);
            var response = result.Adapt<NotifyWalkInCustomerResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Notifies a walk-in customer that their table is ready.")
        .WithName("NotifyWalkInCustomer")
        .Produces<NotifyWalkInCustomerResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
