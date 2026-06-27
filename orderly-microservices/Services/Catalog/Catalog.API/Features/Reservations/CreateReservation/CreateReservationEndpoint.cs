namespace Catalog.API.Features.Reservations.CreateReservation;

public record CreateReservationRequest(
    string CustomerName,
    string CustomerPhone,
    string CustomerEmail,
    LocalDate ReservationDate,
    LocalTime ReservationTime,
    int PartySize,
    bool RequiresApproval,
    string SpecialRequests,
    string Notes);

public record CreateReservationResponse(Guid Id);

public class CreateReservationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Reservations");

        group.MapPost("/restaurants/{restaurantId}/reservations", async (Guid restaurantId, CreateReservationRequest request, ISender sender) =>
        {
            var command = request.Adapt<CreateReservationCommand>() with { RestaurantId = restaurantId };
            var result = await sender.Send(command);
            var response = result.Adapt<CreateReservationResponse>();

            return Results.Created($"/api/v1/restaurants/{restaurantId}/reservations/{response.Id}", response);
        })
        .WithDescription("Creates a new reservation.")
        .WithName("CreateReservation")
        .Produces<CreateReservationResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
