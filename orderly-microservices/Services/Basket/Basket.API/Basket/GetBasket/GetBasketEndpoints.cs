namespace Basket.API.Basket.GetBasket;

public record GetBasketResponse(Models::Basket Basket);

public class GetBasketEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Baskets");

        group.MapGet("/baskets/{userId}/{restaurantId}", async (Guid userId, Guid restaurantId, ISender sender) =>
        {
            var result = await sender.Send(new GetBasketQuery(userId, restaurantId));

            var response = result.Adapt<GetBasketResponse>();

            return Results.Ok(response);
        })
        .WithName("GetBasket")
        .Produces<GetBasketResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Get Basket")
        .WithDescription("Get a user's active shopping basket for a specific restaurant.");
    }
}
