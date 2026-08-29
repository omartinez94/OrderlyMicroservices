namespace Catalog.API.Features.Tables.GetTableById;

public record GetTableByIdResponse(Table Table);

public class GetTableByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Tables");

        group.MapGet("/restaurants/{restaurantId}/tables/{id}", async (Guid id, ISender sender) =>
        {
            GetTableByIdQuery? query = new GetTableByIdQuery(id);
            GetTableByIdResult? result = await sender.Send(query);
            GetTableByIdResponse? response = result.Adapt<GetTableByIdResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a table by ID.")
        .WithName("GetTableById")
        .Produces<GetTableByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
