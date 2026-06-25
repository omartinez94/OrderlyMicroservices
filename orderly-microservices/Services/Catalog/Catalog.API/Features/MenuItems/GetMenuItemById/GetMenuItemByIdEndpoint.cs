namespace Catalog.API.Features.MenuItems.GetMenuItemById;

using Catalog.API.Features.MenuItems.GetMenuItems;

public record GetMenuItemByIdResponse(MenuItemDto MenuItem);

public class GetMenuItemByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/menu-items/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetMenuItemByIdQuery(id));
            return Results.Ok(result.Adapt<GetMenuItemByIdResponse>());
        })
        .WithTags("MenuItems")
        .WithDescription("Gets a menu item by ID.")
        .WithName("GetMenuItemById")
        .Produces<GetMenuItemByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
