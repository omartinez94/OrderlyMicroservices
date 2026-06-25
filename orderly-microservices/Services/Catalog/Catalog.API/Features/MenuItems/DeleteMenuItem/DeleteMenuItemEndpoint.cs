namespace Catalog.API.Features.MenuItems.DeleteMenuItem;

public record DeleteMenuItemResponse(bool Success);

public class DeleteMenuItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/v1/menu-items/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new DeleteMenuItemCommand(id));
            var response = result.Adapt<DeleteMenuItemResponse>();

            return Results.Ok(response);
        })
        .WithTags("MenuItems")
        .WithDescription("Deletes a menu item.")
        .WithName("DeleteMenuItem")
        .Produces<DeleteMenuItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
