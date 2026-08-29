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
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("MenuItemVariations");

        group.MapPost("/menu-items/{menuItemId:guid}/variations", async (Guid menuItemId, CreateMenuItemVariationRequest request, ClaimsPrincipal user, ISender sender) =>
        {
            CreateMenuItemVariationCommand? command = new CreateMenuItemVariationCommand(
                menuItemId,
                request.Name,
                request.VariationValue,
                request.PriceModifier,
                request.IsDefault,
                request.DisplayOrder);

            CreateMenuItemVariationResult? result = await sender.Send(command);
            CreateMenuItemVariationResponse? response = result.Adapt<CreateMenuItemVariationResponse>();

            return Results.Created($"/v1/menu-items/{menuItemId}/variations", response);
        })
        .WithDescription("Creates a new variation for a menu item.")
        .WithName("CreateMenuItemVariation")
        .Produces<CreateMenuItemVariationResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
