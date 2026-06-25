namespace Catalog.API.Features.MenuItemVariations.DeleteMenuItemVariation;

public record DeleteMenuItemVariationResponse(bool Success);

public class DeleteMenuItemVariationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuItemVariations");

        group.MapDelete("/menu-item-variations/{id:int}", async (int id, ISender sender) =>
        {
            var result = await sender.Send(new DeleteMenuItemVariationCommand(id));
            var response = result.Adapt<DeleteMenuItemVariationResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Deletes a menu item variation.")
        .WithName("DeleteMenuItemVariation")
        .Produces<DeleteMenuItemVariationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
