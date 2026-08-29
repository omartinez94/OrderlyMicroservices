namespace Catalog.API.Features.Reservations.GetReservationById;

public record GetReservationByIdResponse(Reservation Reservation);

public class GetReservationByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Reservations");

        group.MapGet("/restaurants/{restaurantId}/reservations/{id}", async (Guid id, ISender sender) =>
        {
            GetReservationByIdQuery? query = new GetReservationByIdQuery(id);
            GetReservationByIdResult? result = await sender.Send(query);
            GetReservationByIdResponse? response = result.Adapt<GetReservationByIdResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a reservation by ID.")
        .WithName("GetReservationById")
        .Produces<GetReservationByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
