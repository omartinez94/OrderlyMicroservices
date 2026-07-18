using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Basket.API.Data;

/// <summary>
/// Redis-fronted cache wrapper around <see cref="BasketRepository"/>.
/// Tenant filtering lives in the inner repository — the cache layer
/// only forwards and serialises. Exceptions from the inner repository
/// (<see cref="ForbiddenException"/>, <see cref="BasketNotFoundException"/>)
/// propagate to the caller without being swallowed.
/// </summary>
public class CachedBasketRepository(IBasketRepository innerRepository, IDistributedCache cache)
    : IBasketRepository
{
    private const int CacheTtlMinutes = 30;

    private static string CacheKey(Guid userId, Guid restaurantId) =>
        $"basket:{userId}:{restaurantId}";

    public async Task<bool> DeleteBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        await innerRepository.DeleteBasketAsync(userId, restaurantId, cancellationToken);

        await cache.RemoveAsync(CacheKey(userId, restaurantId), cancellationToken);

        return true;
    }

    public async Task<Models.Basket> GetBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKey(userId, restaurantId);

        // 1. Try get from Redis
        var cachedBasketInfo = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedBasketInfo))
        {
            return JsonSerializer.Deserialize<Models.Basket>(cachedBasketInfo)!;
        }

        // 2. Not in cache → get from DB (inner throws on miss / forbidden)
        var basket = await innerRepository.GetBasketAsync(userId, restaurantId, cancellationToken);

        // 3. Save to Redis for next time
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheTtlMinutes)
        };

        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(basket), options, cancellationToken);

        return basket;
    }

    public async Task<Models.Basket> GetActiveCartOrEmptyAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKey(userId, restaurantId);

        var cachedBasketInfo = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedBasketInfo))
        {
            return JsonSerializer.Deserialize<Models.Basket>(cachedBasketInfo)!;
        }

        var basket = await innerRepository.GetActiveCartOrEmptyAsync(userId, restaurantId, cancellationToken);

        // Only cache populated carts — an empty cart is cheap to reconstruct
        // from the ids and avoids caching transient "no cart yet" reads.
        if (basket.Items.Count > 0)
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheTtlMinutes)
            };

            await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(basket), options, cancellationToken);
        }

        return basket;
    }

    public async Task<Models.Basket> StoreBasketAsync(Models.Basket basket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basket);

        var storedBasket = await innerRepository.StoreBasketAsync(basket, cancellationToken);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheTtlMinutes)
        };

        await cache.SetStringAsync(
            CacheKey(storedBasket.UserId, storedBasket.RestaurantId),
            JsonSerializer.Serialize(storedBasket),
            options,
            cancellationToken);

        return storedBasket;
    }
}