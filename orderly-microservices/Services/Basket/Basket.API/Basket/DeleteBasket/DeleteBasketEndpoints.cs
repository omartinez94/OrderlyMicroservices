namespace Basket.API.Basket.DeleteBasket;

// public record DeleteBasketRequest(Guid UserId, Guid RestaurantId);
public record DeleteBasketResponse(bool IsSuccess);

public class DeleteBasketEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("/basket/{userId}/{restaurantId}", async (Guid userId, Guid restaurantId, ISender sender) =>
        {
            var result = await sender.Send(new DeleteBasketCommand(userId, restaurantId));

            var response = result.Adapt<DeleteBasketResponse>();

            return Results.Ok(response);
        })
        .WithName("DeleteBasket")
        .Produces<DeleteBasketResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Delete Basket")
        .WithDescription("Delete a user's shopping basket.");
    }
}
