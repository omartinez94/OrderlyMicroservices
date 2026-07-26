namespace Basket.API.Basket.GetBasket;

public record GetBasketResponse(Models::Basket Basket);

public class GetBasketEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapBasketGroup();

        // `[DEPRECATED]`
        // `/api/v1/baskets/{userId}/{restaurantId}` shim is removed.
        // The only route is the token-bound `/api/v1/cart`. The
        // handler returns 200 + empty-cart body when no cart exists
        // (never 404).
        group.MapGet("/cart", async (HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
        {
            var userId = httpContext.User.GetUserId();
            var restaurantId = httpContext.User.GetRestaurantId();
            var result = await sender.Send(new GetBasketQuery(userId, restaurantId), cancellationToken);

            // Cache-Control: no-store — the cart
            // contains PII and must not be cached by intermediaries.
            httpContext.Response.Headers.CacheControl = "no-store";

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
    }
}
