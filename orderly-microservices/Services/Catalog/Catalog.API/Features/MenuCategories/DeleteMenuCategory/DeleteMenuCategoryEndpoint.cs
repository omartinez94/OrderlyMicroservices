namespace Catalog.API.Features.MenuCategories.DeleteMenuCategory;

public record DeleteMenuCategoryResponse(bool IsSuccess);

public class DeleteMenuCategoryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("MenuCategories");

        group.MapDelete("/menu-categories/{id:int}", async (int id, ISender sender) =>
        {
            DeleteMenuCategoryResult? result = await sender.Send(new DeleteMenuCategoryCommand(id));
            DeleteMenuCategoryResponse? response = result.Adapt<DeleteMenuCategoryResponse>();

            return Results.Ok(response);
        })
        .RequirePermission("catalog:menu_update")
        .WithDescription("Deletes a menu category.")
        .WithName("DeleteMenuCategory")
        .Produces<DeleteMenuCategoryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
