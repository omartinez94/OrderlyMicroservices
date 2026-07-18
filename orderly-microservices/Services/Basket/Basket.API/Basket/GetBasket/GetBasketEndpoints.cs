namespace Basket.API.Basket.GetBasket;

public record GetBasketResponse(Models::Basket Basket);

public class GetBasketEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapBasketGroup();

        // Token-bound URL: UserId / RestaurantId come from the JWT, not
        // the path. The BasketIdentityGuardBehavior cross-checks them
        // against the supplied command (defence in depth — for the new
        // shape they are tautologically equal because the endpoint
        // resolves them from the same JWT).
        group.MapGet("/cart", async (HttpContext httpContext, ISender sender) =>
        {
            var userId = httpContext.User.GetUserId();
            var restaurantId = httpContext.User.GetRestaurantId();
            var result = await sender.Send(new GetBasketQuery(userId, restaurantId));
            return Results.Ok(result.Adapt<GetBasketResponse>());
        })
        .WithName("GetBasket")
        .Produces<GetBasketResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithSummary("Get active cart")
        .WithDescription("Returns the authenticated user's active cart for the current restaurant. " +
                         "Returns 200 with an empty cart body when no cart exists (never 404).")
        .RequirePermission("orders:view_own");

        // Deprecated shim — survives Phase 1 for backward compatibility.
        // Same payload, same identity-guard enforcement. Removed end of Phase 3.
        group.MapGet("/baskets/{userId}/{restaurantId}", async (Guid userId, Guid restaurantId, ISender sender) =>
        {
            var result = await sender.Send(new GetBasketQuery(userId, restaurantId));
            return Results.Ok(result.Adapt<GetBasketResponse>());
        })
        .WithName("GetBasket_LegacyShim")
        .Produces<GetBasketResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("[DEPRECATED] Get cart by URL ids — use GET /api/v1/cart")
        .WithDescription("Deprecated route kept for one release. Will be removed end of Phase 3. " +
                         "Use the token-bound GET /api/v1/cart instead — the JWT supplies the identity.")
        .RequirePermission("orders:view_own");
    }
}