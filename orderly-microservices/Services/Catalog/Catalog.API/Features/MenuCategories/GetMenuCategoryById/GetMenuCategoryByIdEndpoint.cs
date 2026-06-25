namespace Catalog.API.Features.MenuCategories.GetMenuCategoryById;

public record GetMenuCategoryByIdResponse(MenuCategoryDto MenuCategory);

public class GetMenuCategoryByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuCategories");

        group.MapGet("/menu-categories/{id:int}", async (int id, ISender sender) =>
        {
            var result = await sender.Send(new GetMenuCategoryByIdQuery(id));
            var response = result.Adapt<GetMenuCategoryByIdResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets a menu category by ID.")
        .WithName("GetMenuCategoryById")
        .Produces<GetMenuCategoryByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
