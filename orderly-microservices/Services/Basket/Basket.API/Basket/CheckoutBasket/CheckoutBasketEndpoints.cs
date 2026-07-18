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
        .WithSummary("Checkout the active cart")
        .WithDescription("Publishes BasketCheckoutEvent and deletes the active cart. " +
                         "Phase 2 will replace this with atomic outbox + idempotency-key handling.")
        .RequirePermission("orders:create");

        // Deprecated shim — body wrapper kept for backward compat.
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