namespace Catalog.API.Features.Reservations.GetReservations;

public record GetReservationsRequest(
    Guid RestaurantId,
    LocalDate? Date = null,
    ReservationStatus? Status = null,
    int? PageNumber = 1,
    int? PageSize = 10);

public record GetReservationsResponse(IEnumerable<Reservation> Reservations, int TotalCount);

public class GetReservationsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Reservations");

        group.MapGet("/restaurants/{restaurantId}/reservations", async ([AsParameters] GetReservationsRequest request, ISender sender) =>
        {
            GetReservationsQuery? query = request.Adapt<GetReservationsQuery>();
            GetReservationsResult? result = await sender.Send(query);
            GetReservationsResponse? response = result.Adapt<GetReservationsResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets reservations for a restaurant, optionally filtered by date and status.")
        .WithName("GetReservations")
        .Produces<GetReservationsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
