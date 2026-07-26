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
public class CachedBasketRepository(
    IBasketRepository innerRepository,
    IDistributedCache cache,
    IBasketCacheLockRegistry cacheLocks)
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

        return await GetOrLoadWithSingleFlightAsync(
            cacheKey,
            innerLookup: ct => innerRepository.GetBasketAsync(userId, restaurantId, ct),
            shouldCache: static _ => true,
            cancellationToken);
    }

    public async Task<Models.Basket> GetActiveCartOrEmptyAsync(Guid userId, Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKey(userId, restaurantId);

        // Cache ALL results, including empty carts. Closes
        // the "empty-cart isn't cached" loop hole: skipping the cache
        // write for empty baskets forced concurrent callers to run
        // the inner query in series — defeating the
        // single-flight coalescing. An empty Basket projection is
        // small (≤ 200 bytes), so caching it does not bloat Redis
        // meaningfully; eliminating the cache-miss round-trip on the
        // common "user opens an empty cart" path is worth the
        // storage.
        return await GetOrLoadWithSingleFlightAsync(
            cacheKey,
            innerLookup: ct => innerRepository.GetActiveCartOrEmptyAsync(userId, restaurantId, ct),
            shouldCache: static _ => true,
            cancellationToken);
    }

    /// <summary>
    /// Canonical <i>single-flight GetOrCreate</i> with double-checked
    /// locking on a per-cache-key <see cref="SemaphoreSlim"/> gate.
    /// Concurrent cache-miss reads on the same
    /// <paramref name="cacheKey"/> collapse onto ONE inner-repository
    /// query — the gate also serialises the cache-write path so the
    /// follow-up caller inside the gate sees a populated cache and
    /// skips the inner round-trip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a re-check inside the gate.</b> A naive gate that
    /// serialises the inner call WITHOUT re-checking the cache lets
    /// N concurrent callers share the gate but still run the inner
    /// query N times in series — the second caller, on acquiring
    /// the gate, observes a still-empty cache (its own outer check
    /// ran before the first caller wrote). The re-check inside the
    /// gate catches this: the first holder writes cache, releases
    /// the gate; the second holder, now inside the gate, sees the
    /// warm cache and returns without re-running the inner.
    /// </para>
    /// <para>
    /// <b>Why the cache-write is inside the gate.</b> If the write
    /// were outside (a release-then-write), the second holder could
    /// acquire and run the inner before the first holder's write
    /// commits — defeating the coalescing. By writing BEFORE
    /// releasing (the <c>await using</c> disposes the handle on
    /// scope exit), the cache is guaranteed to be populated for
    /// the next waiter.
    /// </para>
    /// <para>
    /// <b>Why an outer check AND an inner check.</b> The outer
    /// check is the warm-path fast-read (no gate acquisition cost).
    /// The inner check handles the cold/race path. Together they
    /// collapse concurrent reads to exactly one inner call.
    /// </para>
    /// </remarks>
    private async Task<Models.Basket> GetOrLoadWithSingleFlightAsync(
        string cacheKey,
        Func<CancellationToken, Task<Models.Basket>> innerLookup,
        Func<Models.Basket, bool> shouldCache,
        CancellationToken cancellationToken)
    {
        // Outer fast-path — already-warm cache returns without
        // ever touching the gate.
        var cachedBasketInfo = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedBasketInfo))
        {
            return JsonSerializer.Deserialize<Models.Basket>(cachedBasketInfo, SerializerOptions)!;
        }

        // Cache-miss path. Acquire the gate so concurrent misses on
        // this key serialise through the inner-repository query.
        await using var _handle = await cacheLocks.AcquireAsync(cacheKey, cancellationToken);

        // Re-check inside the gate. The previous holder may have
        // populated the cache before releasing — if so, skip the
        // inner round-trip entirely.
        var cachedRetry = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedRetry))
        {
            return JsonSerializer.Deserialize<Models.Basket>(cachedRetry, SerializerOptions)!;
        }

        // First caller through the gate (or all callers, in the
        // genuine cold-cache case). Run the inner query, then write
        // through to the cache WHILE STILL HOLDING THE GATE so the
        // next holder observes the warmed cache.
        var basket = await innerLookup(cancellationToken);

        if (shouldCache(basket))
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheTtlMinutes),
            };
            await cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(basket, SerializerOptions),
                options,
                cancellationToken);
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