namespace Catalog.API.Features.Tables.GetTables;

public record GetTablesRequest(Guid RestaurantId, TableStatus? Status = null, int? PageNumber = 1, int? PageSize = 10);

public record GetTablesResponse(IEnumerable<Table> Tables, int TotalCount);

public class GetTablesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Tables");

        group.MapGet("/restaurants/{restaurantId}/tables", async ([AsParameters] GetTablesRequest request, ISender sender) =>
        {
            GetTablesQuery? query = request.Adapt<GetTablesQuery>();
            GetTablesResult? result = await sender.Send(query);
            GetTablesResponse? response = result.Adapt<GetTablesResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a list of tables for a restaurant, optionally filtered by status.")
        .WithName("GetTables")
        .Produces<GetTablesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
