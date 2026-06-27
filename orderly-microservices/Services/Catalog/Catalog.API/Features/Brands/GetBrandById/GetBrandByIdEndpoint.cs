namespace Catalog.API.Features.Brands.GetBrandById;

public record GetBrandByIdResponse(Brand Brand);

public class GetBrandByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Brands");

        group.MapGet("/brands/{id}", async (Guid id, ISender sender) =>
        {
            var query = new GetBrandByIdQuery(id);
            var result = await sender.Send(query);
            var response = result.Adapt<GetBrandByIdResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a brand by ID.")
        .WithName("GetBrandById")
        .Produces<GetBrandByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
