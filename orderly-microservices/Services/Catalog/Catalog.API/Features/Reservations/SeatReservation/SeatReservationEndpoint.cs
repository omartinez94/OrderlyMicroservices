namespace Catalog.API.Features.Reservations.SeatReservation;

public record SeatReservationRequest(Guid? TableId = null);

public record SeatReservationResponse(bool IsSuccess);

public class SeatReservationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Reservations");

        group.MapPut("/restaurants/{restaurantId}/reservations/{id}/seat", async (Guid id, SeatReservationRequest request, ISender sender) =>
        {
            SeatReservationCommand? command = new SeatReservationCommand(id, request.TableId);
            SeatReservationResult? result = await sender.Send(command);
            SeatReservationResponse? response = result.Adapt<SeatReservationResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Seats a reservation and optionally assigns a table.")
        .WithName("SeatReservation")
        .Produces<SeatReservationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
