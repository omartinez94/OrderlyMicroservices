namespace Catalog.API.Features.Brands.GetBrands;

public record GetBrandsRequest(int? PageNumber = 1, int? PageSize = 10);

public record GetBrandsResponse(IEnumerable<Brand> Brands);

public class GetBrandsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Brands");

        group.MapGet("/brands", async ([AsParameters] GetBrandsRequest request, ISender sender) =>
        {
            var query = request.Adapt<GetBrandsQuery>();
            var result = await sender.Send(query);
            var response = result.Adapt<GetBrandsResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a list of all brands.")
        .WithName("GetBrands")
        .Produces<GetBrandsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
