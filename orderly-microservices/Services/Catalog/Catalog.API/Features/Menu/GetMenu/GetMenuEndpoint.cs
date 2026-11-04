namespace Catalog.API.Features.Menu.GetMenu;

public sealed class GetMenuEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/restaurants/{restaurantId:guid}/menu", async (Guid restaurantId, ISender sender, CancellationToken cancellationToken) =>
        {
            GetMenuQuery query = new(restaurantId);
            GetMenuResult result = await sender.Send(query, cancellationToken);

            return Results.Ok(result.Snapshot);
        })
        .RequirePermission("menu:view")
        .WithTags("Menu")
        .WithDescription("Gets the cached full menu tree snapshot for a restaurant.")
        .WithName("GetMenu")
        .Produces<MenuSnapshot>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
