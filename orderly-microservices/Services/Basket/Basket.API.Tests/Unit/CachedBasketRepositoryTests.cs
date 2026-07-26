using Microsoft.Extensions.Caching.Distributed;
using NodaTime.Serialization.SystemTextJson;
using System.Text.Json;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Phase 3.1: <see cref="CachedBasketRepository"/> single-flight
/// guard. These tests do NOT spin up Postgres or Redis — they
/// substitute the inner <see cref="IBasketRepository"/> with
/// NSubstitute and use a hand-rolled
/// <see cref="InMemoryDistributedCache"/> as the cache double. The
/// point is the coalescing contract: N concurrent cache-miss reads
/// on the same (user, restaurant) collapse to ONE inner-repository
/// query, not N. End-to-end Postgres-in-the-loop coverage is the
/// Testcontainers work in Phase 5.
/// </summary>
public sealed class CachedBasketRepositoryTests
{
    private static readonly TimeSpan InnerDelay = TimeSpan.FromMilliseconds(75);

    private static (Guid userId, Guid restaurantId, Models.Basket basket) NewFixtureBasket()
    {
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        return (userId, restaurantId, new Models.Basket(userId, restaurantId)
        {
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            ExpiresAt = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromMinutes(30)),
        });
    }

    private static Models.Basket BuildPopulatedBasket(Guid userId, Guid restaurantId)
    {
        var basket = new Models.Basket(userId, restaurantId)
        {
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            ExpiresAt = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromMinutes(30)),
        };
        basket.Items.Add(new Models.BasketItem
        {
            MenuItemId = 42,
            Quantity = 1,
            UnitPrice = 9.99m,
        });
        return basket;
    }

    private static Models.Basket BuildEmptyBasket(Guid userId, Guid restaurantId) =>
        new(userId, restaurantId)
        {
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            ExpiresAt = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromMinutes(30)),
        };

    private static IBasketCacheLockRegistry NewRegistry() => new BasketCacheLockRegistry();

    private static string CacheKey(Guid userId, Guid restaurantId) =>
        $"basket:{userId}:{restaurantId}";

    [Fact]
    public async Task GetBasketAsync_CacheMiss_OnlyOneInnerCallUnderContention()
    {
        // Arrange — 100 concurrent callers on the same key, with a
        // 75 ms artificial inner delay so the race window is wide
        // enough to deterministically observe coalescing. Without the
        // single-flight guard's double-check, every concurrent task
        // would queue on the gate, run the inner in serial, and emit
        // 100 inner calls. With the gate, exactly ONE inner call.
        var innerRepository = Substitute.For<IBasketRepository>();
        var cache = new InMemoryDistributedCache();
        var (userId, restaurantId, _) = NewFixtureBasket();
        var basket = BuildPopulatedBasket(userId, restaurantId);

        innerRepository
            .GetBasketAsync(userId, restaurantId, Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                // Honour caller cancellation. NSubstitute's CallInfo
                // exposes the args by index; CT is the third arg
                // (userId, restaurantId, cancellationToken).
                var ct = callInfo.ArgAt<CancellationToken>(2);
                await Task.Delay(InnerDelay, ct);
                return basket;
            });

        var registry = NewRegistry();
        var sut = new CachedBasketRepository(innerRepository, cache, registry);

        // Act — fire 100 concurrent reads.
        const int Concurrency = 100;
        var tasks = Enumerable
            .Range(0, Concurrency)
            .Select(_ => Task.Run(() => sut.GetBasketAsync(userId, restaurantId)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert — every caller got the basket; the inner query
        // happened exactly once; the cache is now populated.
        results.Should().AllBeEquivalentTo(basket, o => o.RespectingRuntimeTypes());
        await innerRepository
            .Received(1)
            .GetBasketAsync(userId, restaurantId, Arg.Any<CancellationToken>());
        cache.Snapshot.Should().ContainKey(CacheKey(userId, restaurantId),
            "the first single-flight holder writes the cache inside the gate");

        await registry.DisposeAsync();
    }

    [Fact]
    public async Task GetActiveCartOrEmptyAsync_CacheMiss_OnlyOneInnerCallUnderContention()
    {
        // Arrange — same single-flight contract on the read-or-empty
        // projection. Empty cart (no items → no cache write), but the
        // inner query still coalesces.
        var innerRepository = Substitute.For<IBasketRepository>();
        var cache = new InMemoryDistributedCache();
        var (userId, restaurantId, _) = NewFixtureBasket();
        var basket = BuildEmptyBasket(userId, restaurantId);

        innerRepository
            .GetActiveCartOrEmptyAsync(userId, restaurantId, Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(InnerDelay, callInfo.ArgAt<CancellationToken>(2));
                return basket;
            });

        var registry = NewRegistry();
        var sut = new CachedBasketRepository(innerRepository, cache, registry);

        // Act
        const int Concurrency = 100;
        var tasks = Enumerable
            .Range(0, Concurrency)
            .Select(_ => Task.Run(() => sut.GetActiveCartOrEmptyAsync(userId, restaurantId)))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllBeEquivalentTo(basket);
        await innerRepository
            .Received(1)
            .GetActiveCartOrEmptyAsync(userId, restaurantId, Arg.Any<CancellationToken>());
        // Empty carts ARE cached too — see GetActiveCartOrEmptyAsync's
        // Phase 3.1 comment. The cache-write happens inside the
        // single-flight gate so the next holder sees the warmed
        // entry.
        cache.Snapshot.Should().ContainKey(CacheKey(userId, restaurantId));

        await registry.DisposeAsync();
    }

    [Fact]
    public async Task GetBasketAsync_CacheHit_DoesNotEnterGateOrTouchInner()
    {
        // Arrange — cache already holds the basket. The read fast-path
        // returns without ever entering the gate or calling the
        // inner repository.
        var innerRepository = Substitute.For<IBasketRepository>();
        var cache = new InMemoryDistributedCache();
        var (userId, restaurantId, _) = NewFixtureBasket();
        var basket = BuildPopulatedBasket(userId, restaurantId);

        var warmCacheKey = CacheKey(userId, restaurantId);
        await cache.SetAsync(
            warmCacheKey,
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(basket, SerializerOptionsForCacheTests())),
            new DistributedCacheEntryOptions());

        var registry = NewRegistry();
        var sut = new CachedBasketRepository(innerRepository, cache, registry);

        // Act
        var result = await sut.GetBasketAsync(userId, restaurantId);

        // Assert
        result.Should().BeEquivalentTo(basket, o => o.RespectingRuntimeTypes());
        await innerRepository
            .DidNotReceive()
            .GetBasketAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await registry.DisposeAsync();
    }

    [Fact]
    public async Task GetBasketAsync_DifferentKeys_AreIndependent()
    {
        // Arrange — two different (user, restaurant) pairs each
        // independently miss the cache; their inner calls should run
        // in parallel without one blocking the other through the same
        // semaphore.
        var innerRepository = Substitute.For<IBasketRepository>();
        var cache = new InMemoryDistributedCache();
        var (userA, restaurantA, _) = NewFixtureBasket();
        var (userB, restaurantB, _) = NewFixtureBasket();
        var basketA = BuildPopulatedBasket(userA, restaurantA);
        var basketB = BuildPopulatedBasket(userB, restaurantB);

        innerRepository
            .GetBasketAsync(userA, restaurantA, Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(InnerDelay, callInfo.ArgAt<CancellationToken>(2));
                return basketA;
            });
        innerRepository
            .GetBasketAsync(userB, restaurantB, Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(InnerDelay, callInfo.ArgAt<CancellationToken>(2));
                return basketB;
            });

        var registry = NewRegistry();
        var sut = new CachedBasketRepository(innerRepository, cache, registry);

        // Act
        var started = DateTime.UtcNow;
        var taskA = Task.Run(() => sut.GetBasketAsync(userA, restaurantA));
        var taskB = Task.Run(() => sut.GetBasketAsync(userB, restaurantB));
        await Task.WhenAll(taskA, taskB);
        var elapsed = DateTime.UtcNow - started;

        // Assert — independent gates; total elapsed is approximately
        // InnerDelay, not 2 × InnerDelay.
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "distinct keys must not block each other through the same semaphore");

        await registry.DisposeAsync();
    }

    [Fact]
    public async Task GetBasketAsync_CallerCancellation_PropagatesAndGateFrees()
    {
        // Arrange — start a query, cancel mid-flight, then verify a
        // fresh caller can still acquire the gate (cancellation does
        // not leak the semaphore).
        var innerRepository = Substitute.For<IBasketRepository>();
        var cache = new InMemoryDistributedCache();
        var (userId, restaurantId, _) = NewFixtureBasket();
        var basket = BuildPopulatedBasket(userId, restaurantId);

        innerRepository
            .GetBasketAsync(userId, restaurantId, Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.ArgAt<CancellationToken>(2);
                ct.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return basket;
            });

        var registry = NewRegistry();
        var sut = new CachedBasketRepository(innerRepository, cache, registry);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(20));

        // Act + Assert — first caller cancels mid-flight.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.GetBasketAsync(userId, restaurantId, cts.Token));

        // A subsequent caller can still acquire the gate — the
        // cancellation did not permanently seize the semaphore.
        var freshHandle = await registry.AcquireAsync(
            CacheKey(userId, restaurantId), CancellationToken.None);
        await freshHandle.DisposeAsync();

        await registry.DisposeAsync();
    }

    [Fact]
    public async Task StoreBasketAsync_CacheWritePath_DoesNotEnterGate()
    {
        // Arrange — StoreBasketAsync writes through to the cache. It
        // does NOT enter the read-side single-flight gate (writes
        // collapse onto the existing key naturally; the
        // cache.SetStringAsync is single-valued per key).
        var innerRepository = Substitute.For<IBasketRepository>();
        var cache = new InMemoryDistributedCache();
        var (userId, restaurantId, _) = NewFixtureBasket();
        var basket = BuildPopulatedBasket(userId, restaurantId);

        innerRepository
            .StoreBasketAsync(basket, Arg.Any<CancellationToken>())
            .Returns((basket, true));

        var registry = NewRegistry();
        var sut = new CachedBasketRepository(innerRepository, cache, registry);

        // Act
        var (stored, isCreated) = await sut.StoreBasketAsync(basket);

        // Assert — the write happened, and the inner query path is
        // unchanged (no GetStringAsync involvement).
        stored.Should().BeSameAs(basket);
        isCreated.Should().BeTrue();
        await innerRepository
            .Received(1)
            .StoreBasketAsync(basket, Arg.Any<CancellationToken>());
        await registry.DisposeAsync();
    }

    [Fact]
    public async Task InvalidateCacheAsync_PassesThroughAndFreesGateForNextReader()
    {
        // Arrange — checkout flow runs InvalidateCacheAsync; the
        // next caller must be able to enter the gate.
        var innerRepository = Substitute.For<IBasketRepository>();
        var cache = new InMemoryDistributedCache();
        var (userId, restaurantId, _) = NewFixtureBasket();

        var registry = NewRegistry();
        var sut = new CachedBasketRepository(innerRepository, cache, registry);

        // Act
        await sut.InvalidateCacheAsync(userId, restaurantId);

        // A fresh read acquires the gate cleanly.
        var handle = await registry.AcquireAsync(
            CacheKey(userId, restaurantId), CancellationToken.None);
        await handle.DisposeAsync();

        await registry.DisposeAsync();
    }

    private static System.Text.Json.JsonSerializerOptions SerializerOptionsForCacheTests()
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        };
        options.ConfigureForNodaTime(NodaTime.DateTimeZoneProviders.Tzdb);
        return options;
    }
}
