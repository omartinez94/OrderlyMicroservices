namespace Catalog.API.Features.Reservations.ConfirmReservation;

public record ConfirmReservationResponse(bool IsSuccess);

public class ConfirmReservationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Reservations");

        group.MapPut("/restaurants/{restaurantId}/reservations/{id}/confirm", async (Guid id, ISender sender) =>
        {
            ConfirmReservationCommand? command = new ConfirmReservationCommand(id);
            ConfirmReservationResult? result = await sender.Send(command);
            ConfirmReservationResponse? response = result.Adapt<ConfirmReservationResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Confirms a reservation.")
        .WithName("ConfirmReservation")
        .Produces<ConfirmReservationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
