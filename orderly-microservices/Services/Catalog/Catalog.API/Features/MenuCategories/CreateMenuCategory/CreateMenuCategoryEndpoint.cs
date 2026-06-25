namespace Catalog.API.Features.MenuCategories.CreateMenuCategory;

public record CreateMenuCategoryRequest(
    string Name,
    string Description,
    int DisplayOrder);

public record CreateMenuCategoryResponse(int Id);

public class CreateMenuCategoryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuCategories");

        group.MapPost("/restaurants/{restaurantId:guid}/menu-categories", async (Guid restaurantId, CreateMenuCategoryRequest request, ClaimsPrincipal user, ISender sender) =>
        {
            var command = new CreateMenuCategoryCommand(restaurantId, request.Name, request.Description, request.DisplayOrder);

            var result = await sender.Send(command);
            var response = result.Adapt<CreateMenuCategoryResponse>();

            return Results.Created($"/api/v1/menu-categories/{response.Id}", response);
        })
        .WithDescription("Creates a new menu category for a restaurant.")
        .WithName("CreateMenuCategory")
        .Produces<CreateMenuCategoryResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
