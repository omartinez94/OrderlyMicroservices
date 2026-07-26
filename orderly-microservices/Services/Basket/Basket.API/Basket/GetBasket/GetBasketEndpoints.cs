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

            // ETag + Last-Modified. The ETag is the
            // SHA-256 of the basket JSON projection (cheap; the
            // basket is small, <1 KB). Last-Modified is the
            // basket's LastModifiedAt. Both headers drive
            // conditional GET — a client sending
            // If-None-Match / If-Modified-Since that matches sees
            // 304 Not Modified with no body.
            var basket = result.Basket;
            var etag = global::Basket.API.Caching.ETag.Compute(basket);
            httpContext.Response.Headers.ETag = $"\"{etag}\"";
            var lastModified = basket.LastModifiedAt == default
                ? basket.CreatedAt
                : basket.LastModifiedAt;
            httpContext.Response.Headers.LastModified = lastModified.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            // Conditional GET — short-circuit to 304 BEFORE the
            // body serialisation so we don't pay the JSON cost.
            if (global::Basket.API.Caching.ETag.IsNotModified(httpContext.Request, etag, lastModified))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            return Results.Ok(result.Adapt<GetBasketResponse>());
        })
        .WithName("GetBasket")
        .Produces<GetBasketResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status304NotModified)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithSummary("Get active cart")
        .WithDescription("Returns the authenticated user's active cart for the current restaurant. " +
                         "Returns 200 with an empty cart body when no cart exists (never 404). " +
                         "Supports conditional GET: If-None-Match (ETag) and If-Modified-Since " +
                         "(Last-Modified) both return 304 Not Modified with no body on cache hit.")
        .RequirePermission("orders:view_own");
    }
}
