namespace Basket.API.Data;

public class BasketRepository(IDocumentSession session)
    : IBasketRepository
{
    public async Task<bool> DeleteBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var basket = await session.Query<Models::Basket>()
            .Where(b => b.UserId == userId && b.RestaurantId == restaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (basket is not null)
        {
            session.Delete(basket);
            await session.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<Models.Basket> GetBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var basket = await session.Query<Models::Basket>()
            .Where(b => b.UserId == userId && b.RestaurantId == restaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        return basket is null ? throw new BasketNotFoundException(userId, restaurantId) : basket;
    }

    public async Task<Models.Basket> StoreBasketAsync(Models.Basket basket, CancellationToken cancellationToken = default)
    {
        var existingBasket = await session.Query<Models::Basket>()
            .Where(b => b.UserId == basket.UserId && b.RestaurantId == basket.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingBasket is not null)
        {
            // Using ID assignment if Basket had one, but we assume entity replacement here.
            session.Delete(existingBasket);
        }

        session.Store(basket);
        await session.SaveChangesAsync(cancellationToken);

        return basket;
    }
}
