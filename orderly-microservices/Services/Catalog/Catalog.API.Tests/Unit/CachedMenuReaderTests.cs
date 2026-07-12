using System.Text;
using System.Text.Json;
using Catalog.API.Readers;
using Microsoft.Extensions.Caching.Distributed;

namespace Catalog.API.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="CachedMenuReader"/> covering the cache-on-read
/// decorator's hit / miss / null / fail-open paths.
/// </summary>
/// <remarks>
/// <see cref="IDistributedCache.GetStringAsync(string, CancellationToken)"/> and
/// <see cref="IDistributedCache.SetStringAsync(string, string, DistributedCacheEntryOptions, CancellationToken)"/>
/// are <em>extension methods</em> on <see cref="IDistributedCache"/> (defined in
/// <c>DistributedCacheExtensions</c>), not interface members. NSubstitute can
/// only intercept virtual/abstract members, so the tests configure
/// <see cref="IDistributedCache.GetAsync(string, CancellationToken)"/> and
/// <see cref="IDistributedCache.SetAsync(string, byte[], DistributedCacheEntryOptions, CancellationToken)"/>
/// — the underlying interface methods that the extensions call.
/// </remarks>
public sealed class CachedMenuReaderTests
{
    private const string RestaurantIdString = "11111111-2222-3333-4444-555555555555";
    private static readonly Guid RestaurantId = Guid.Parse(RestaurantIdString);
    private static readonly string CacheKey = CacheKeys.Menu(RestaurantId);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
    };

    private static MenuSnapshot BuildSnapshot(Guid restaurantId) =>
        new(
            RestaurantId: restaurantId,
            SnapshotAt: NodaTime.SystemClock.Instance.GetCurrentInstant(),
            Categories: []);

    private static CachedMenuReader BuildSut(
        IMenuReader inner,
        IDistributedCache cache,
        CatalogOptions? options = null)
    {
        var monitor = Substitute.For<IOptionsMonitor<CatalogOptions>>();
        monitor.CurrentValue.Returns(options ?? new CatalogOptions());
        return new CachedMenuReader(
            inner,
            cache,
            monitor,
            NullLogger<CachedMenuReader>.Instance);
    }

    [Fact]
    public async Task GetByRestaurantAsync_CacheHit_ReturnsCachedSnapshotWithoutCallingInner()
    {
        // Arrange
        var inner = Substitute.For<IMenuReader>();
        var cache = Substitute.For<IDistributedCache>();
        var snapshot = BuildSnapshot(RestaurantId);
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, SerializerOptions));
        cache.GetAsync(CacheKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(payload));
        var sut = BuildSut(inner, cache);

        // Act
        var result = await sut.GetByRestaurantAsync(RestaurantId);

        // Assert
        result.Should().NotBeNull();
        result!.RestaurantId.Should().Be(RestaurantId);
        await inner.DidNotReceive().GetByRestaurantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByRestaurantAsync_CacheMiss_CallsInnerAndPopulatesCacheWithConfiguredTtl()
    {
        // Arrange
        var inner = Substitute.For<IMenuReader>();
        var cache = Substitute.For<IDistributedCache>();
        var snapshot = BuildSnapshot(RestaurantId);
        cache.GetAsync(CacheKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        inner.GetByRestaurantAsync(RestaurantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MenuSnapshot?>(snapshot));
        var options = new CatalogOptions { MenuCacheTtlMinutes = 42 };
        var sut = BuildSut(inner, cache, options);

        // Act
        var result = await sut.GetByRestaurantAsync(RestaurantId);

        // Assert
        result.Should().Be(snapshot);
        await inner.Received(1).GetByRestaurantAsync(RestaurantId, Arg.Any<CancellationToken>());
        await cache.Received(1).SetAsync(
            CacheKey,
            Arg.Is<byte[]>(b => Encoding.UTF8.GetString(b).Contains(RestaurantIdString)),
            Arg.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(42)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByRestaurantAsync_InnerReturnsNull_DoesNotWriteToCache()
    {
        // Arrange
        var inner = Substitute.For<IMenuReader>();
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(CacheKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        inner.GetByRestaurantAsync(RestaurantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MenuSnapshot?>(null));
        var sut = BuildSut(inner, cache);

        // Act
        var result = await sut.GetByRestaurantAsync(RestaurantId);

        // Assert
        result.Should().BeNull();
        await cache.DidNotReceive().SetAsync(
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByRestaurantAsync_CacheReadThrows_FallsThroughToInner()
    {
        // Arrange
        var inner = Substitute.For<IMenuReader>();
        var cache = Substitute.For<IDistributedCache>();
        var snapshot = BuildSnapshot(RestaurantId);
        cache
            .When(x => x.GetAsync(CacheKey, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Redis offline"));
        inner.GetByRestaurantAsync(RestaurantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MenuSnapshot?>(snapshot));
        var sut = BuildSut(inner, cache);

        // Act
        var result = await sut.GetByRestaurantAsync(RestaurantId);

        // Assert — fail-open: inner is called, snapshot is returned.
        result.Should().Be(snapshot);
        await inner.Received(1).GetByRestaurantAsync(RestaurantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByRestaurantAsync_CacheWriteThrows_StillReturnsInnerResult()
    {
        // Arrange
        var inner = Substitute.For<IMenuReader>();
        var cache = Substitute.For<IDistributedCache>();
        var snapshot = BuildSnapshot(RestaurantId);
        cache.GetAsync(CacheKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        inner.GetByRestaurantAsync(RestaurantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MenuSnapshot?>(snapshot));
        cache
            .When(x => x.SetAsync(
                Arg.Any<string>(),
                Arg.Any<byte[]>(),
                Arg.Any<DistributedCacheEntryOptions>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Redis write failed"));
        var sut = BuildSut(inner, cache);

        // Act
        var result = await sut.GetByRestaurantAsync(RestaurantId);

        // Assert — fail-open on write too.
        result.Should().Be(snapshot);
        await inner.Received(1).GetByRestaurantAsync(RestaurantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_NullArguments_Throws()
    {
        var inner = Substitute.For<IMenuReader>();
        var cache = Substitute.For<IDistributedCache>();
        var monitor = Substitute.For<IOptionsMonitor<CatalogOptions>>();

        Assert.Throws<ArgumentNullException>(() =>
            new CachedMenuReader(null!, cache, monitor, NullLogger<CachedMenuReader>.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new CachedMenuReader(inner, null!, monitor, NullLogger<CachedMenuReader>.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new CachedMenuReader(inner, cache, null!, NullLogger<CachedMenuReader>.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new CachedMenuReader(inner, cache, monitor, null!));
    }
}