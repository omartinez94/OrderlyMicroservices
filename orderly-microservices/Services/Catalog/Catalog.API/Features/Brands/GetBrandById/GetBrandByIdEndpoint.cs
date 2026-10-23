namespace Catalog.API.Features.Brands.GetBrandById;

public record GetBrandByIdResponse(Brand Brand);

public class GetBrandByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Brands");

        group.MapGet("/brands/{id}", async (string id, ISender sender) =>
        {
            GetBrandByIdQuery? query = new GetBrandByIdQuery(id);
            GetBrandByIdResult? result = await sender.Send(query);
            GetBrandByIdResponse? response = result.Adapt<GetBrandByIdResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a brand by id or short id.")
        .WithName("GetBrandById")
        .Produces<GetBrandByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
