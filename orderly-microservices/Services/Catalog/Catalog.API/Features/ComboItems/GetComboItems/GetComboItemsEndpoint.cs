namespace Catalog.API.Features.ComboItems.GetComboItems;

public record GetComboItemsResponse(IEnumerable<ComboItemDto> ComboItems);

public record ComboItemDto(int Id, Guid ComboMenuItemId, Guid IncludedMenuItemId, int Quantity, bool IsOptional);

public class GetComboItemsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("ComboItems");

        group.MapGet("/menu-items/{comboMenuItemId}/combo-items", async (Guid comboMenuItemId, ISender sender) =>
        {
            GetComboItemsResult? result = await sender.Send(new GetComboItemsQuery(comboMenuItemId));
            GetComboItemsResponse? response = result.Adapt<GetComboItemsResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Gets all combo items for a menu item.")
        .WithName("GetComboItems")
        .Produces<GetComboItemsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
