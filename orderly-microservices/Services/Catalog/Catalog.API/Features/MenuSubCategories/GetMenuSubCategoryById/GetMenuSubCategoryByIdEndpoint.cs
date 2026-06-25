namespace Catalog.API.Features.MenuSubCategories.GetMenuSubCategoryById;

public record GetMenuSubCategoryByIdResponse(MenuSubCategoryDto SubCategory);

public record MenuSubCategoryDto(
    int Id,
    int CategoryId,
    string Name,
    string Description,
    int DisplayOrder,
    bool IsActive);

public class GetMenuSubCategoryByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/menu-sub-categories/{id}", async (int id, ISender sender) =>
        {
            var result = await sender.Send(new GetMenuSubCategoryByIdQuery(id));
            var response = result.Adapt<GetMenuSubCategoryByIdResponse>();

            return Results.Ok(response);
        })
        .WithTags("MenuSubCategories")
        .WithDescription("Gets a menu sub-category by its Id.")
        .WithName("GetMenuSubCategoryById")
        .Produces<GetMenuSubCategoryByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
