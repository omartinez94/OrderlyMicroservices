namespace Catalog.API.Features.MenuCategories.UpdateMenuCategory;

public record UpdateMenuCategoryRequest(
    string Name,
    string Description,
    int DisplayOrder);

public record UpdateMenuCategoryResponse(bool IsSuccess);

public class UpdateMenuCategoryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuCategories");

        group.MapPut("/menu-categories/{id:int}", async (int id, UpdateMenuCategoryRequest request, ISender sender) =>
        {
            var command = new UpdateMenuCategoryCommand(id, request.Name, request.Description, request.DisplayOrder);

            var result = await sender.Send(command);
            var response = result.Adapt<UpdateMenuCategoryResponse>();

            return Results.Ok(response);
        })
        .RequirePermission("catalog:menu_update")
        .WithDescription("Updates a menu category.")
        .WithName("UpdateMenuCategory")
        .Produces<UpdateMenuCategoryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
