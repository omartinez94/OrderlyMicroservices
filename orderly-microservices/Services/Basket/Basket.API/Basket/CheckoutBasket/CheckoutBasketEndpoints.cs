namespace Basket.API.Basket.CheckoutBasket;

public record CheckoutBasketRequest(BasketCheckoutDto BasketCheckoutDto);
public record CheckoutBasketResponse(bool Success, string Message);

public class CheckoutBasketEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapBasketGroup();

        // Token-bound URL: the BasketIdentityGuardBehavior cross-checks
        // the BasketCheckoutDto's UserId/RestaurantId against the JWT
        // and throws ForbiddenException on mismatch.
        // Phase 2.3: BasketIdempotencyFilter enforces the IETF
        // draft-ietf-httpapi-idempotency-key-header contract — required
        // UUID v4 header, replays on body match, 422 on body mismatch.
        group.MapPost("/cart/checkout", async (BasketCheckoutDto checkoutDto, ISender sender) =>
        {
            var result = await sender.Send(new CheckoutBasketCommand(checkoutDto));
            return result.Success
                ? Results.Ok(result.Adapt<CheckoutBasketResponse>())
                : Results.BadRequest(result.Adapt<CheckoutBasketResponse>());
        })
        .WithName("CheckoutBasket")
        .Produces<CheckoutBasketResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .WithSummary("Checkout the active cart")
        .WithDescription("Publishes BasketCheckoutEvent and deletes the active cart. " +
                         "Phase 2: atomic outbox + idempotency-key handling.")
        .RequirePermission("orders:create")
        .AddEndpointFilter<BasketIdempotencyFilter>()
        // Phase 2.4: rate limiter — 5 requests/minute per
        // (userId, restaurantId). The other three endpoints stay
        // unlimited per plan §0.4.8.
        .RequireRateLimiting("checkout");

        // Deprecated shim — body wrapper kept for backward compat.
        // Idempotency is NOT applied to the shim (it'll be removed at
        // end of Phase 3 — adding idempotency to a route on the way
        // out is wasted work).
        group.MapPost("/baskets/checkout", async (CheckoutBasketRequest request, ISender sender) =>
        {
            var command = request.Adapt<CheckoutBasketCommand>();
            var result = await sender.Send(command);
            return result.Success
                ? Results.Ok(result.Adapt<CheckoutBasketResponse>())
                : Results.BadRequest(result.Adapt<CheckoutBasketResponse>());
        })
        .WithName("CheckoutBasket_LegacyShim")
        .Produces<CheckoutBasketResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithSummary("[DEPRECATED] Checkout by URL — use POST /api/v1/cart/checkout")
        .WithDescription("Deprecated route kept for one release. Will be removed end of Phase 3.")
        .RequirePermission("orders:create");
    }
}