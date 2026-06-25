namespace Catalog.API.Features.MenuItemVariations.CreateMenuItemVariation;

public record CreateMenuItemVariationRequest(
    string Name,
    string VariationValue,
    decimal PriceModifier,
    bool IsDefault,
    int DisplayOrder);

public record CreateMenuItemVariationResponse(int Id);

public class CreateMenuItemVariationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuItemVariations");

        group.MapPost("/menu-items/{menuItemId:guid}/variations", async (Guid menuItemId, CreateMenuItemVariationRequest request, ClaimsPrincipal user, ISender sender) =>
        {
            var command = new CreateMenuItemVariationCommand(
                menuItemId,
                request.Name,
                request.VariationValue,
                request.PriceModifier,
                request.IsDefault,
                request.DisplayOrder);

            var result = await sender.Send(command);
            var response = result.Adapt<CreateMenuItemVariationResponse>();

            return Results.Created($"/api/v1/menu-items/{menuItemId}/variations", response);
        })
        .WithDescription("Creates a new variation for a menu item.")
        .WithName("CreateMenuItemVariation")
        .Produces<CreateMenuItemVariationResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
