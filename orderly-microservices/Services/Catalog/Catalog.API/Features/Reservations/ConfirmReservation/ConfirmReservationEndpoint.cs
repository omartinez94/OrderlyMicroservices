namespace Catalog.API.Features.Reservations.ConfirmReservation;

public record ConfirmReservationResponse(bool IsSuccess);

public class ConfirmReservationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Reservations");

        group.MapPut("/restaurants/{restaurantId}/reservations/{id}/confirm", async (Guid id, ISender sender) =>
        {
            var command = new ConfirmReservationCommand(id);
            var result = await sender.Send(command);
            var response = result.Adapt<ConfirmReservationResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Confirms a reservation.")
        .WithName("ConfirmReservation")
        .Produces<ConfirmReservationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
