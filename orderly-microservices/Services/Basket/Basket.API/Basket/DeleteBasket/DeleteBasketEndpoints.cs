namespace Basket.API.Basket.DeleteBasket;

public class DeleteBasketEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapBasketGroup();

        // DELETE returns 204 No Content. The cart
        // deletion is idempotent — repeating the call on a cart that
        // no longer exists returns 204, not 404.
        // `[DEPRECATED]` /api/v1/baskets/{userId}/{restaurantId} shim
        // is removed; the only route is the token-bound
        // `/api/v1/cart`. The endpoint carried a
        // `DeleteBasketResponse { IsSuccess = true }` body; the
        // 204-No-Content contract eliminates the body entirely.
        group.MapDelete("/cart", async (HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
        {
            var userId = httpContext.User.GetUserId();
            var restaurantId = httpContext.User.GetRestaurantId();
            await sender.Send(new DeleteBasketCommand(userId, restaurantId), cancellationToken);

            // Cache-Control: no-store — the cart
            // contains PII and must not be cached by intermediaries.
            httpContext.Response.Headers.CacheControl = "no-store";

            return Results.NoContent();
        })
        .WithName("DeleteBasket")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithSummary("Abandon active cart")
        .WithDescription("Deletes the authenticated user's active cart for the current restaurant. " +
                         "Returns 204 No Content (idempotent — deleting a non-existent cart also returns 204).")
        .RequirePermission("orders:create");
    }
}
