namespace Basket.API.Basket.StoreBasket;

public record StoreBasketRequest(Models::Basket Basket);
public record StoreBasketResponse(Guid UserId, Guid RestaurantId);

public class StoreBasketEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapBasketGroup();

        // Token-bound URL: the BasketIdentityGuardBehavior cross-checks
        // the body's UserId/RestaurantId against the JWT and throws
        // ForbiddenException on mismatch. The caller can supply any
        // values in the body — the JWT is authoritative.
        group.MapPut("/cart", async (Models.Basket basket, ISender sender) =>
        {
            var result = await sender.Send(new StoreBasketCommand(basket));
            return Results.Ok(result.Adapt<StoreBasketResponse>());
        })
        .WithName("StoreBasket")
        .Produces<StoreBasketResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithSummary("Upsert active cart")
        .WithDescription("Creates or replaces the authenticated user's active cart for the current restaurant. " +
                         "The body's UserId / RestaurantId must match the JWT (enforced by the identity guard).")
        .RequirePermission("orders:create");

        // Deprecated shim — same payload, same identity-guard enforcement.
        group.MapPut("/baskets/{userId}/{restaurantId}", async (Guid userId, Guid restaurantId, StoreBasketRequest request, ISender sender) =>
        {
            var command = request.Adapt<StoreBasketCommand>();
            var result = await sender.Send(command);
            return Results.Ok(result.Adapt<StoreBasketResponse>());
        })
        .WithName("StoreBasket_LegacyShim")
        .Produces<StoreBasketResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithSummary("[DEPRECATED] Upsert cart by URL ids — use PUT /api/v1/cart")
        .WithDescription("Deprecated route kept for one release. Will be removed end of Phase 3.")
        .RequirePermission("orders:create");
    }
}