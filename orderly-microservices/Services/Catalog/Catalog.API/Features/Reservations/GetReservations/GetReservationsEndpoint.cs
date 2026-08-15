namespace Catalog.API.Features.Reservations.GetReservations;

/// <remarks>
/// Date-valued query parameters are typed <see cref="DateOnly"/>, not NodaTime
/// <c>LocalDate</c>. Minimal API binds an <c>[AsParameters]</c> property from
/// the query string only when its type is parsable (<c>IParsable&lt;T&gt;</c> or
/// a static <c>TryParse</c>); <c>LocalDate</c> is neither, so the binder falls
/// back to inferring it as a *body* parameter, which is illegal on GET. That
/// throws while the endpoint graph is being built, taking down every route in
/// the service — not just this one. <c>DateOnly</c> is <c>IParsable</c>, accepts
/// the same ISO-8601 <c>yyyy-MM-dd</c> wire format, and renders as
/// <c>format: date</c> in OpenAPI. Convert to <c>LocalDate</c> at the handler
/// boundary via <c>LocalDate.FromDateOnly</c>.
/// </remarks>
public record GetReservationsRequest(
    Guid RestaurantId,
    DateOnly? Date = null,
    ReservationStatus? Status = null,
    int? PageNumber = 1,
    int? PageSize = 10);

public record GetReservationsResponse(IEnumerable<Reservation> Reservations, int TotalCount);

public class GetReservationsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Reservations");

        group.MapGet("/restaurants/{restaurantId}/reservations", async ([AsParameters] GetReservationsRequest request, ISender sender) =>
        {
            // Constructed explicitly rather than via Adapt: the request's
            // DateOnly? does not map to the query's LocalDate? without a
            // registered Mapster converter, and a silently-dropped date
            // filter would return every reservation instead of failing.
            var query = new GetReservationsQuery(
                request.RestaurantId,
                request.Date is { } date ? LocalDate.FromDateOnly(date) : null,
                request.Status,
                request.PageNumber,
                request.PageSize);

            var result = await sender.Send(query);
            var response = result.Adapt<GetReservationsResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets reservations for a restaurant, optionally filtered by date and status.")
        .WithName("GetReservations")
        .Produces<GetReservationsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
