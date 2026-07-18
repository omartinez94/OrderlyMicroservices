namespace Basket.API.Basket.DeleteBasket;

public record DeleteBasketResponse(bool IsSuccess);

public class DeleteBasketEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapBasketGroup();

        group.MapDelete("/cart", async (HttpContext httpContext, ISender sender) =>
        {
            var userId = httpContext.User.GetUserId();
            var restaurantId = httpContext.User.GetRestaurantId();
            var result = await sender.Send(new DeleteBasketCommand(userId, restaurantId));
            return Results.Ok(result.Adapt<DeleteBasketResponse>());
        })
        .WithName("DeleteBasket")
        .Produces<DeleteBasketResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithSummary("Abandon active cart")
        .WithDescription("Deletes the authenticated user's active cart for the current restaurant. " +
                         "Idempotent: deleting a non-existent cart returns 200 with IsSuccess = true.")
        .RequirePermission("orders:create");

        // Deprecated shim.
        group.MapDelete("/baskets/{userId}/{restaurantId}", async (Guid userId, Guid restaurantId, ISender sender) =>
        {
            var result = await sender.Send(new DeleteBasketCommand(userId, restaurantId));
            return Results.Ok(result.Adapt<DeleteBasketResponse>());
        })
        .WithName("DeleteBasket_LegacyShim")
        .Produces<DeleteBasketResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("[DEPRECATED] Abandon cart by URL ids — use DELETE /api/v1/cart")
        .WithDescription("Deprecated route kept for one release. Will be removed end of Phase 3.")
        .RequirePermission("orders:create");
    }
}