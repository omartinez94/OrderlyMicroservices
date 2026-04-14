
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Basket.API.Data;

public class CachedBasketRepository(IBasketRepository innerRepository, IDistributedCache cache)
    : IBasketRepository
{
    public async Task<bool> DeleteBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var result = await innerRepository.DeleteBasketAsync(userId, restaurantId, cancellationToken);
        
        var cacheKey = $"basket:{userId}:{restaurantId}";
        await cache.RemoveAsync(cacheKey, cancellationToken);
        
        return result;
    }

    public async Task<Models.Basket> GetBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"basket:{userId}:{restaurantId}";
    
        // 1. Try get from Redis
        var cachedBasketInfo = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedBasketInfo))
        {
            return JsonSerializer.Deserialize<Models.Basket>(cachedBasketInfo)!;
        }

        // 2. Not in cache → get from DB
        var basket = await innerRepository.GetBasketAsync(userId, restaurantId, cancellationToken);

        // 3. Save to Redis for next time
        if (basket is not null)
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) // e.g. 30 min TTL
            };

            await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(basket), options, cancellationToken);
        }

        return basket is null ? throw new BasketNotFoundException(userId, restaurantId) : basket;
    }

    public async Task<Models.Basket> StoreBasketAsync(Models.Basket basket, CancellationToken cancellationToken = default)
    {
        var storedBasket = await innerRepository.StoreBasketAsync(basket, cancellationToken);

        var cacheKey = $"basket:{basket.UserId}:{basket.RestaurantId}";
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        };
        
        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(storedBasket), options, cancellationToken);

        return storedBasket;
    }
}
