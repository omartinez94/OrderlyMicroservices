using NodaTime.Serialization.SystemTextJson;
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

    /// <summary>
    /// Cache-side JSON options. Mirrors the global HTTP pipeline
    /// registration in <c>Program.cs</c>:
    /// <c>PropertyNamingPolicy = null</c> (PascalCase on the wire) +
    /// <c>ConfigureForNodaTime</c> (round-trips <see cref="NodaTime.Instant"/>
    /// without losing precision).
    /// </summary>
    /// <remarks>
    /// Without <c>ConfigureForNodaTime</c>, the default System.Text.Json
    /// configuration serialises <see cref="NodaTime.Instant"/> as an
    /// empty object — a silent round-trip break that surfaces only when
    /// a cached basket is read back with a default-constructed
    /// <c>CreatedAt = default</c> / <c>ExpiresAt = default</c>.
    /// Rewrite of this layer did NOT add the shared options; closes the drift item.
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    static CachedBasketRepository()
    {
        SerializerOptions.ConfigureForNodaTime(NodaTime.DateTimeZoneProviders.Tzdb);
    }

    private static string CacheKey(Guid userId, Guid restaurantId) =>
        $"basket:{userId}:{restaurantId}";

    public async Task<bool> DeleteBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        await innerRepository.DeleteBasketAsync(userId, restaurantId, cancellationToken);

        await cache.RemoveAsync(CacheKey(userId, restaurantId), cancellationToken);

        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Cache-only — does not touch Marten. The checkout handler calls
    /// this AFTER its <c>IDocumentSession.SaveChangesAsync()</c>
    /// commits the outbox row + basket delete; invalidating here closes
    /// the window where a concurrent reader could see a deleted basket
    /// in the cache.
    /// </remarks>
    public async Task InvalidateCacheAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default) =>
        await cache.RemoveAsync(CacheKey(userId, restaurantId), cancellationToken);

    public async Task<Models.Basket> GetBasketAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKey(userId, restaurantId);

        // 1. Try get from Redis
        var cachedBasketInfo = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedBasketInfo))
        {
            return JsonSerializer.Deserialize<Models.Basket>(cachedBasketInfo, SerializerOptions)!;
        }

        // 2. Not in cache → get from DB (inner throws on miss / forbidden)
        var basket = await innerRepository.GetBasketAsync(userId, restaurantId, cancellationToken);

        // 3. Save to Redis for next time
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheTtlMinutes)
        };

        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(basket, SerializerOptions), options, cancellationToken);

        return basket;
    }

    public async Task<Models.Basket> GetActiveCartOrEmptyAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKey(userId, restaurantId);

        var cachedBasketInfo = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedBasketInfo))
        {
            return JsonSerializer.Deserialize<Models.Basket>(cachedBasketInfo, SerializerOptions)!;
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

            await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(basket, SerializerOptions), options, cancellationToken);
        }

        return basket;
    }

    public async Task<(Models.Basket Basket, bool IsCreated)> StoreBasketAsync(Models.Basket basket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basket);

        var (storedBasket, isCreated) = await innerRepository.StoreBasketAsync(basket, cancellationToken);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheTtlMinutes)
        };

        await cache.SetStringAsync(
            CacheKey(storedBasket.UserId, storedBasket.RestaurantId),
            JsonSerializer.Serialize(storedBasket, SerializerOptions),
            options,
            cancellationToken);

        return (storedBasket, isCreated);
    }
}