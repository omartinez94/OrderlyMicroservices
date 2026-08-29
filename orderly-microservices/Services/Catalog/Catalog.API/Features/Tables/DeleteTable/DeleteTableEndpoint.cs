namespace Catalog.API.Features.Tables.DeleteTable;

public record DeleteTableResponse(bool IsSuccess);

public class DeleteTableEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Tables");

        group.MapDelete("/restaurants/{restaurantId}/tables/{id}", async (Guid id, ISender sender) =>
        {
            DeleteTableCommand? command = new DeleteTableCommand(id);
            DeleteTableResult? result = await sender.Send(command);
            DeleteTableResponse? response = result.Adapt<DeleteTableResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Deletes a table.")
        .WithName("DeleteTable")
        .Produces<DeleteTableResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
