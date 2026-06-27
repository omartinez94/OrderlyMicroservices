namespace Catalog.API.Features.Brands.UpdateBrand;

public record UpdateBrandRequest(
    string Name,
    string Description,
    string LogoUrl,
    string WebsiteUrl,
    string ContactEmail,
    string ContactPhone,
    CuisineType CuisineType);

public record UpdateBrandResponse(bool IsSuccess);

public class UpdateBrandEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Brands");

        group.MapPut("/brands/{id}", async (Guid id, UpdateBrandRequest request, ISender sender) =>
        {
            var command = request.Adapt<UpdateBrandCommand>() with { Id = id };
            var result = await sender.Send(command);
            var response = result.Adapt<UpdateBrandResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Updates a brand.")
        .WithName("UpdateBrand")
        .Produces<UpdateBrandResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
