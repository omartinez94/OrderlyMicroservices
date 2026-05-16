namespace Basket.API.Data;

public interface IBasketRepository
{
    Task<Models::Basket> GetBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default);

    Task<Models::Basket> StoreBasketAsync(Models::Basket basket, CancellationToken cancellationToken = default);

    Task<bool> DeleteBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default);
}
