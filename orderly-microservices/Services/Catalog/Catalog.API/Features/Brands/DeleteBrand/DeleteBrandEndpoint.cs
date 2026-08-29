namespace Catalog.API.Features.Brands.DeleteBrand;

public record DeleteBrandResponse(bool IsSuccess);

public class DeleteBrandEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("Brands");

        group.MapDelete("/brands/{id}", async (Guid id, ISender sender) =>
        {
            DeleteBrandCommand? command = new DeleteBrandCommand(id);
            DeleteBrandResult? result = await sender.Send(command);
            DeleteBrandResponse? response = result.Adapt<DeleteBrandResponse>();

            return Results.Ok(response);
        })
        .RequirePermission("catalog:menu_update")
        .WithDescription("Deletes a brand.")
        .WithName("DeleteBrand")
        .Produces<DeleteBrandResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
