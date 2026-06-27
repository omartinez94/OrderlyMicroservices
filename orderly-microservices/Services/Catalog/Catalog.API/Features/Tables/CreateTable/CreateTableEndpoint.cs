namespace Catalog.API.Features.Tables.CreateTable;

public record CreateTableRequest(
    Guid RestaurantId,
    string TableNumber,
    int Capacity,
    string Shape,
    int PositionX,
    int PositionY);

public record CreateTableResponse(Guid Id);

public class CreateTableEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Tables");

        group.MapPost("/restaurants/{restaurantId}/tables", async (Guid restaurantId, CreateTableRequest request, ISender sender) =>
        {
            var command = request.Adapt<CreateTableCommand>();
            var result = await sender.Send(command);
            var response = result.Adapt<CreateTableResponse>();

            return Results.Created($"/api/v1/restaurants/{restaurantId}/tables/{response.Id}", response);
        })
        .WithDescription("Creates a new table for a restaurant.")
        .WithName("CreateTable")
        .Produces<CreateTableResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
