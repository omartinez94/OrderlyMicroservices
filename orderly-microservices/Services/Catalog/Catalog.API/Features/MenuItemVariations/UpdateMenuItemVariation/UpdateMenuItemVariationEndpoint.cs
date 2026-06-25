namespace Catalog.API.Features.MenuItemVariations.UpdateMenuItemVariation;

public record UpdateMenuItemVariationRequest(
    string Name,
    string VariationValue,
    decimal PriceModifier,
    bool IsDefault,
    int DisplayOrder);

public record UpdateMenuItemVariationResponse(bool Success);

public class UpdateMenuItemVariationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("MenuItemVariations");

        group.MapPut("/menu-item-variations/{id:int}", async (int id, UpdateMenuItemVariationRequest request, ISender sender) =>
        {
            var command = new UpdateMenuItemVariationCommand(
                id,
                request.Name,
                request.VariationValue,
                request.PriceModifier,
                request.IsDefault,
                request.DisplayOrder);

            var result = await sender.Send(command);
            var response = result.Adapt<UpdateMenuItemVariationResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Updates a menu item variation.")
        .WithName("UpdateMenuItemVariation")
        .Produces<UpdateMenuItemVariationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
