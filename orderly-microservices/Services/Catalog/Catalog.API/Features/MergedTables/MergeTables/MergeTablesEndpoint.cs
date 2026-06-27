namespace Catalog.API.Features.MergedTables.MergeTables;

public record MergeTablesRequest(Guid ParentTableId, Guid ChildTableId);

public record MergeTablesResponse(Guid Id);

public class MergeTablesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MergedTables");

        group.MapPost("/restaurants/{restaurantId}/merged-tables", async (Guid restaurantId, MergeTablesRequest request, ISender sender) =>
        {
            var command = new MergeTablesCommand(restaurantId, request.ParentTableId, request.ChildTableId);

            var result = await sender.Send(command);
            var response = result.Adapt<MergeTablesResponse>();

            return Results.Created($"/api/v1/restaurants/{restaurantId}/merged-tables/{response.Id}", response);
        })
        .WithDescription("Merges two tables.")
        .WithName("MergeTables")
        .Produces<MergeTablesResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
