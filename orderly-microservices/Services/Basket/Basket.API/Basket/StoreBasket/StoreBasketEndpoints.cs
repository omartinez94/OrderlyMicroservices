namespace Basket.API.Basket.StoreBasket;
public record StoreBasketRequest(Models::Basket Basket);
public record StoreBasketResponse(Guid UserId, Guid RestaurantId);

public class StoreBasketEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Baskets");

        group.MapPut("/baskets/{userId}/{restaurantId}", async (Guid userId, Guid restaurantId, StoreBasketRequest request, ISender sender) =>
        {
            var command = request.Adapt<StoreBasketCommand>();

            var result = await sender.Send(command);

            var response = result.Adapt<StoreBasketResponse>();

            return Results.Ok(response);
        })
        .WithName("StoreBasket")
        .Produces<StoreBasketResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("Store Basket")
        .WithDescription("Create or update a user's shopping basket.");
    }
}
