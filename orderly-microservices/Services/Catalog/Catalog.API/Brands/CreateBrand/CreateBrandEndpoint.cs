namespace Catalog.API.Brands.CreateBrand;

public record CreateBrandRequest(
    string Name,
    string Description,
    string LogoUrl,
    string WebsiteUrl,
    string ContactEmail,
    string ContactPhone,
    CuisineType CuisineType,
    bool IsActive);

public record CreateBrandResponse(Guid Id);

public class CreateBrandEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/brands", async (CreateBrandRequest request, ISender sender) =>
        {
            var command = request.Adapt<CreateBrandCommand>();
            var result = await sender.Send(command);
            var response = result.Adapt<CreateBrandResponse>();

            return Results.Created($"/brands/{response.Id}", response);
        })
        .WithDescription("Creates a new brand.")
        .WithName("CreateBrand")
        .Produces<CreateBrandResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
