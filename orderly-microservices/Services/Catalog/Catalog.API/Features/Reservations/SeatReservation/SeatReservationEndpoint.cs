namespace Catalog.API.Features.Reservations.SeatReservation;

public record SeatReservationRequest(Guid? TableId = null);

public record SeatReservationResponse(bool IsSuccess);

public class SeatReservationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Reservations");

        group.MapPut("/restaurants/{restaurantId}/reservations/{id}/seat", async (Guid id, SeatReservationRequest request, ISender sender) =>
        {
            var command = new SeatReservationCommand(id, request.TableId);
            var result = await sender.Send(command);
            var response = result.Adapt<SeatReservationResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Seats a reservation and optionally assigns a table.")
        .WithName("SeatReservation")
        .Produces<SeatReservationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
