namespace Catalog.API.Features.MergedTables.GetMergedTables;

public record GetMergedTablesResponse(IEnumerable<MergedTableDto> MergedTables);

public record MergedTableDto(Guid Id, Guid ParentTableId, Guid ChildTableId, bool IsActive, Instant MergedAt, Instant? SplitAt);

public class GetMergedTablesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MergedTables");

        group.MapGet("/restaurants/{restaurantId}/merged-tables", async (Guid restaurantId, ISender sender) =>
        {
            var result = await sender.Send(new GetMergedTablesQuery(restaurantId));
            var response = result.Adapt<GetMergedTablesResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets merged tables for a restaurant.")
        .WithName("GetMergedTables")
        .Produces<GetMergedTablesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
