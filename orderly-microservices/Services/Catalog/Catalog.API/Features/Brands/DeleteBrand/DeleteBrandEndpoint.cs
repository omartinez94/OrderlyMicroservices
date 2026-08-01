namespace Catalog.API.Features.Brands.DeleteBrand;

public record DeleteBrandResponse(bool IsSuccess);

public class DeleteBrandEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Brands");

        group.MapDelete("/brands/{id}", async (Guid id, ISender sender) =>
        {
            var command = new DeleteBrandCommand(id);
            var result = await sender.Send(command);
            var response = result.Adapt<DeleteBrandResponse>();

            return Results.Ok(response);
        })
        .RequirePermission("catalog:menu_update")
        .WithDescription("Deletes a brand.")
        .WithName("DeleteBrand")
        .Produces<DeleteBrandResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
