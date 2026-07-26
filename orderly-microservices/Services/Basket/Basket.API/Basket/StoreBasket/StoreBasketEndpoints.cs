namespace Basket.API.Basket.StoreBasket;

public record StoreBasketResponse(bool IsCreated, Guid UserId, Guid RestaurantId);

public class StoreBasketEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapBasketGroup();

        // `[DEPRECATED]`
        // `/api/v1/baskets/{userId}/{restaurantId}` shim is removed.
        // The only route is the token-bound `/api/v1/cart`; the
        // `StoreBasketRequest(Basket)` wrapper record is no longer
        // needed because the body shape is now `Models.Basket`
        // directly.
        group.MapPut("/cart", async (System.Security.Claims.ClaimsPrincipal principal, Models.Basket basket, ISender sender, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            // spoofing-footgun fix: the body MUST carry Guid.Empty
            // for UserId / RestaurantId; the validator rejects any other
            // value with 422 before we get here. The endpoint overwrites
            // with the JWT-derived identity so the BasketIdentityGuardBehavior
            // sees matching values when it cross-checks the
            // command's identity against the JWT.
            basket.UserId = principal.GetUserId();
            basket.RestaurantId = principal.GetRestaurantId();

            var result = await sender.Send(new StoreBasketCommand(basket), cancellationToken);

            // Cache-Control: no-store — the cart
            // contains PII and must not be cached by intermediaries.
            httpContext.Response.Headers.CacheControl = "no-store";

            // PUT semantics: 201 Created + Location: /api/v1/cart on
            // a new cart, 200 OK on update. Idempotent — repeated PUTs
            // with the same body converge to the same final state.
            return result.IsCreated
                ? Results.Created("/api/v1/cart", result.Adapt<StoreBasketResponse>())
                : Results.Ok(result.Adapt<StoreBasketResponse>());
        })
        .WithName("StoreBasket")
        .Produces<StoreBasketResponse>(StatusCodes.Status201Created)
        .Produces<StoreBasketResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .WithSummary("Upsert active cart")
        .WithDescription("Creates or replaces the authenticated user's active cart for the current restaurant. " +
                         "Returns 201 Created + Location: /api/v1/cart on the first PUT (new cart) and 200 OK on every subsequent PUT (idempotent upsert). " +
                         "The body's UserId / RestaurantId MUST be Guid.Empty; the JWT-derived identity is authoritative.")
        .RequirePermission("orders:create");
    }
}
