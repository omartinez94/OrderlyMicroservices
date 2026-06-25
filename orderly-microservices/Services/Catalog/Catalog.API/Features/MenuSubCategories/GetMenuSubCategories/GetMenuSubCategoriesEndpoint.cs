namespace Catalog.API.Features.MenuSubCategories.GetMenuSubCategories;

public record GetMenuSubCategoriesResponse(IEnumerable<MenuSubCategoryDto> SubCategories);

public record MenuSubCategoryDto(
    int Id,
    int CategoryId,
    string Name,
    string Description,
    int DisplayOrder,
    bool IsActive);

public class GetMenuSubCategoriesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/menu-categories/{categoryId}/sub-categories", async (int categoryId, ISender sender) =>
        {
            var result = await sender.Send(new GetMenuSubCategoriesQuery(categoryId));
            var response = result.Adapt<GetMenuSubCategoriesResponse>();

            return Results.Ok(response);
        })
        .WithTags("MenuSubCategories")
        .WithDescription("Gets a list of sub-categories for a specific menu category.")
        .WithName("GetMenuSubCategories")
        .Produces<GetMenuSubCategoriesResponse>(StatusCodes.Status200OK);
    }
}
