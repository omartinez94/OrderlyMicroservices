namespace Basket.API.Exceptions;

public class BasketNotFoundException(Guid userId, Guid restaurantId) : NotFoundException("Basket", new { UserId = userId, RestaurantId = restaurantId })
{
}
