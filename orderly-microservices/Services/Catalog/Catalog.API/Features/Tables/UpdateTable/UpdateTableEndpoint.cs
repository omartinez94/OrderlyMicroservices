namespace Catalog.API.Features.Tables.UpdateTable;

public record UpdateTableRequest(
    string TableNumber,
    int Capacity,
    string Shape,
    int PositionX,
    int PositionY,
    TableStatus Status,
    Guid? CurrentOrderId);

public record UpdateTableResponse(bool IsSuccess);

public class UpdateTableEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Tables");

        group.MapPut("/restaurants/{restaurantId}/tables/{id}", async (Guid id, UpdateTableRequest request, ISender sender) =>
        {
            var command = request.Adapt<UpdateTableCommand>() with { Id = id };
            var result = await sender.Send(command);
            var response = result.Adapt<UpdateTableResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Updates a table.")
        .WithName("UpdateTable")
        .Produces<UpdateTableResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
