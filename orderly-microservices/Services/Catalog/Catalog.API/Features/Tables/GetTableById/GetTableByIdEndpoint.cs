namespace Catalog.API.Features.Tables.GetTableById;

public record GetTableByIdResponse(Table Table);

public class GetTableByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Tables");

        group.MapGet("/restaurants/{restaurantId}/tables/{id}", async (Guid id, ISender sender) =>
        {
            var query = new GetTableByIdQuery(id);
            var result = await sender.Send(query);
            var response = result.Adapt<GetTableByIdResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a table by ID.")
        .WithName("GetTableById")
        .Produces<GetTableByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
