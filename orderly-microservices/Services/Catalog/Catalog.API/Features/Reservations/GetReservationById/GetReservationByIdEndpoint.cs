namespace Catalog.API.Features.Reservations.GetReservationById;

public record GetReservationByIdResponse(Reservation Reservation);

public class GetReservationByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Reservations");

        group.MapGet("/restaurants/{restaurantId}/reservations/{id}", async (Guid id, ISender sender) =>
        {
            var query = new GetReservationByIdQuery(id);
            var result = await sender.Send(query);
            var response = result.Adapt<GetReservationByIdResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a reservation by ID.")
        .WithName("GetReservationById")
        .Produces<GetReservationByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
