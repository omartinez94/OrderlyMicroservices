namespace Catalog.API.Features.MenuSubCategories.DeleteMenuSubCategory;

public record DeleteMenuSubCategoryResponse(bool Success);

public class DeleteMenuSubCategoryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuSubCategories");

        group.MapDelete("/menu-sub-categories/{id:int}", async (int id, ISender sender) =>
        {
            await sender.Send(new DeleteMenuSubCategoryCommand(id));
            return Results.Ok(new DeleteMenuSubCategoryResponse(true));
        })
        .WithDescription("Soft-deletes a menu sub-category. Idempotent: a second delete on an already-deleted row is a no-op.")
        .WithName("DeleteMenuSubCategory")
        .Produces<DeleteMenuSubCategoryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}