namespace Catalog.API.Features.MergedTables.SplitTables;

public class SplitTablesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MergedTables");

        group.MapDelete("/restaurants/{restaurantId}/merged-tables/{id}", async (Guid restaurantId, Guid id, ISender sender) =>
        {
            await sender.Send(new SplitTablesCommand(restaurantId, id));

            return Results.NoContent();
        })
        .WithDescription("Splits previously merged tables.")
        .WithName("SplitTables")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
