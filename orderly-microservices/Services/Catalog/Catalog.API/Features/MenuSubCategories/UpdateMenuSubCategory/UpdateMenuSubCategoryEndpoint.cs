namespace Catalog.API.Features.MenuSubCategories.UpdateMenuSubCategory;

public record UpdateMenuSubCategoryRequest(
    int CategoryId,
    string Name,
    string Description,
    int DisplayOrder,
    bool IsActive);

public record UpdateMenuSubCategoryResponse(bool Success);

public class UpdateMenuSubCategoryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/v1/menu-sub-categories/{id}", async (int id, UpdateMenuSubCategoryRequest request, ISender sender) =>
        {
            UpdateMenuSubCategoryCommand? command = request.Adapt<UpdateMenuSubCategoryCommand>() with { Id = id };

            UpdateMenuSubCategoryResult? result = await sender.Send(command);
            UpdateMenuSubCategoryResponse? response = result.Adapt<UpdateMenuSubCategoryResponse>();

            return Results.Ok(response);
        })
        .WithTags("MenuSubCategories")
        .WithDescription("Updates a menu sub-category.")
        .WithName("UpdateMenuSubCategory")
        .Produces<UpdateMenuSubCategoryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
