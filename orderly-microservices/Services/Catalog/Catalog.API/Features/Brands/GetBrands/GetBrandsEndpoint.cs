namespace Catalog.API.Features.Brands.GetBrands;

public record GetBrandsRequest(int? PageNumber = 1, int? PageSize = 10);

public record GetBrandsResponse(IEnumerable<Brand> Brands);

public class GetBrandsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Brands");

        group.MapGet("/brands", async ([AsParameters] GetBrandsRequest request, ISender sender) =>
        {
            GetBrandsQuery? query = request.Adapt<GetBrandsQuery>();
            GetBrandsResult? result = await sender.Send(query);
            GetBrandsResponse? response = result.Adapt<GetBrandsResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a list of all brands.")
        .WithName("GetBrands")
        .Produces<GetBrandsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
