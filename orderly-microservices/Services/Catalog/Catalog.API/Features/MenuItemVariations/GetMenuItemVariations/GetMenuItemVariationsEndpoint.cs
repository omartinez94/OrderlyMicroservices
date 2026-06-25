namespace Catalog.API.Features.MenuItemVariations.GetMenuItemVariations;

public record GetMenuItemVariationsResponse(IEnumerable<MenuItemVariationDto> Variations);

public record MenuItemVariationDto(
    int Id,
    Guid MenuItemId,
    string Name,
    string VariationValue,
    decimal PriceModifier,
    bool IsDefault,
    int DisplayOrder);

public class GetMenuItemVariationsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuItemVariations");

        group.MapGet("/menu-items/{menuItemId:guid}/variations", async (Guid menuItemId, ISender sender) =>
        {
            var result = await sender.Send(new GetMenuItemVariationsQuery(menuItemId));
            var response = result.Adapt<GetMenuItemVariationsResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets variations for a menu item.")
        .WithName("GetMenuItemVariations")
        .Produces<GetMenuItemVariationsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
