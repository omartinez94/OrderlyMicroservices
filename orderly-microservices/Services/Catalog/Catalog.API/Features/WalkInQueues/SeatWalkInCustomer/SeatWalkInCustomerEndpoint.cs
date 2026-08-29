namespace Catalog.API.Features.WalkInQueues.SeatWalkInCustomer;

public record SeatWalkInCustomerRequest(Guid TableId);

public record SeatWalkInCustomerResponse(bool IsSuccess);

public class SeatWalkInCustomerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("WalkInQueues");

        group.MapPut("/restaurants/{restaurantId}/walk-in-queue/{id}/seat", async (int id, SeatWalkInCustomerRequest request, ISender sender) =>
        {
            SeatWalkInCustomerCommand? command = new SeatWalkInCustomerCommand(id, request.TableId);
            SeatWalkInCustomerResult? result = await sender.Send(command);
            SeatWalkInCustomerResponse? response = result.Adapt<SeatWalkInCustomerResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Seats a walk-in customer and assigns a table.")
        .WithName("SeatWalkInCustomer")
        .Produces<SeatWalkInCustomerResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
