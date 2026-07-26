namespace Basket.API.Basket.CheckoutBasket;

public record CheckoutBasketResponse(bool Success, string Message);

public class CheckoutBasketEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapBasketGroup();

        // `/api/v1/baskets/checkout` shim is removed. The only route is
        // the token-bound `/api/v1/cart/checkout`; the
        // `CheckoutBasketRequest(BasketCheckoutDto)` wrapper record is
        // no longer needed because the body shape is the DTO directly.
        group.MapPost("/cart/checkout", async (System.Security.Claims.ClaimsPrincipal principal, BasketCheckoutDto checkoutDto, ISender sender, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            // spoofing-footgun fix: the body MUST carry Guid.Empty
            // for UserId / RestaurantId; the validator rejects any other
            // value with 422 before we get here. The endpoint overwrites
            // with the JWT-derived identity so the BasketIdentityGuardBehavior
            // sees matching values when it cross-checks the
            // command's identity against the JWT.
            checkoutDto.UserId = principal.GetUserId();
            checkoutDto.RestaurantId = principal.GetRestaurantId();

            var result = await sender.Send(new CheckoutBasketCommand(checkoutDto), cancellationToken);

            // Cache-Control: no-store — the checkout
            // response carries the receipt summary (PII-trimmed) and
            // must not be cached by intermediaries.
            httpContext.Response.Headers.CacheControl = "no-store";

            return result.Success
                ? Results.Ok(result.Adapt<CheckoutBasketResponse>())
                : Results.BadRequest(result.Adapt<CheckoutBasketResponse>());
        })
        .WithName("CheckoutBasket")
        .Produces<CheckoutBasketResponse>(StatusCodes.Status200OK)
        .Produces<CheckoutBasketResponse>(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .WithSummary("Checkout the active cart")
        .WithDescription("Publishes BasketCheckoutEvent and deletes the active cart. " +
                         "Phase 2: atomic outbox + idempotency-key handling.")
        .RequirePermission("orders:create")
        .AddEndpointFilter<BasketIdempotencyFilter>()
        // Rate limiter — 5 requests/minute per
        // (userId, restaurantId). The other three endpoints stay unlimited.
        .RequireRateLimiting("checkout");
    }
}
