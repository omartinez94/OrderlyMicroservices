namespace Catalog.API.Features.Brands.CreateBrand;

public record CreateBrandRequest(
    string Name,
    string Description,
    string LogoUrl,
    string WebsiteUrl,
    string ContactEmail,
    string ContactPhone,
    CuisineType CuisineType,
    bool IsActive);

public record CreateBrandResponse(Guid Id, string ShortId = "");

public class CreateBrandEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Brands");

        group.MapPost("/brands", async (CreateBrandRequest request, ISender sender) =>
        {
            CreateBrandCommand? command = request.Adapt<CreateBrandCommand>();
            CreateBrandResult? result = await sender.Send(command);
            CreateBrandResponse? response = result.Adapt<CreateBrandResponse>();

            return Results.Created($"/v1/brands/{response.Id}", response);
        })
        .RequirePermission("catalog:menu_update")
        .WithDescription("Creates a new brand.")
        .WithName("CreateBrand")
        .Produces<CreateBrandResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
