namespace Catalog.API.Features.Reservations.CancelReservation;

public record CancelReservationRequest(string? Reason = null);

public record CancelReservationResponse(bool IsSuccess);

public class CancelReservationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Reservations");

        group.MapPut("/restaurants/{restaurantId}/reservations/{id}/cancel", async (Guid id, CancelReservationRequest request, ISender sender) =>
        {
            var command = new CancelReservationCommand(id, request.Reason);
            var result = await sender.Send(command);
            var response = result.Adapt<CancelReservationResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Cancels a reservation.")
        .WithName("CancelReservation")
        .Produces<CancelReservationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
