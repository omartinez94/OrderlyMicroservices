namespace Catalog.API.Features.MenuSubCategories.CreateMenuSubCategory;

public record CreateMenuSubCategoryRequest(
    string Name,
    string Description,
    int DisplayOrder,
    bool IsActive);

public record CreateMenuSubCategoryResponse(int Id);

public class CreateMenuSubCategoryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuSubCategories");

        group.MapPost("/menu-categories/{categoryId}/sub-categories", async (int categoryId, CreateMenuSubCategoryRequest request, ISender sender) =>
        {
            var command = request.Adapt<CreateMenuSubCategoryCommand>() with { CategoryId = categoryId };

            var result = await sender.Send(command);
            var response = result.Adapt<CreateMenuSubCategoryResponse>();

            return Results.Created($"/api/v1/menu-sub-categories/{response.Id}", response);
        })
        .WithDescription("Creates a new menu sub-category.")
        .WithName("CreateMenuSubCategory")
        .Produces<CreateMenuSubCategoryResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
