namespace Basket.API.Tests.Unit;

/// <summary>
/// Phase 4: <see cref="ETag"/> strong-tag computation + conditional
/// request handling. The endpoint-level integration is Phase 5
/// (WebApplicationFactory); the unit tests lock the helper
/// invariants.
/// </summary>
public sealed class ETagTests
{
    [Fact]
    public void Compute_DifferentBaskets_ProducesDifferentETags()
    {
        // Arrange
        var basket1 = new Models.Basket(Guid.NewGuid(), Guid.NewGuid())
        {
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            ExpiresAt = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromMinutes(30)),
        };
        var basket2 = new Models.Basket(Guid.NewGuid(), Guid.NewGuid())
        {
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            ExpiresAt = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromMinutes(30)),
        };

        // Act
        var etag1 = ETag.Compute(basket1);
        var etag2 = ETag.Compute(basket2);

        // Assert — different ids → different SHA-256 → different etags.
        etag1.Should().NotBe(etag2);
        etag1.Should().NotBeNullOrEmpty();
        etag2.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Compute_SameBasket_ProducesSameETag()
    {
        // Arrange — the helper is deterministic. Two projections of
        // the same cart must hash to the same value.
        var now = SystemClock.Instance.GetCurrentInstant();
        var basket1 = new Models.Basket(Guid.NewGuid(), Guid.NewGuid())
        {
            CreatedAt = now,
            ExpiresAt = now.Plus(Duration.FromMinutes(30)),
        };
        var basket2 = new Models.Basket(basket1.UserId, basket1.RestaurantId)
        {
            CreatedAt = now,
            ExpiresAt = now.Plus(Duration.FromMinutes(30)),
        };

        // Act
        var etag1 = ETag.Compute(basket1);
        var etag2 = ETag.Compute(basket2);

        // Assert
        etag1.Should().Be(etag2);
    }

    [Fact]
    public void IsNotModified_IfNoneMatchMatches_ReturnsTrue()
    {
        // Arrange
        var basket = NewBasket();
        var etag = ETag.Compute(basket);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.IfNoneMatch = $"\"{etag}\"";

        // Act + Assert
        ETag.IsNotModified(ctx.Request, etag, basket.LastModifiedAt).Should().BeTrue();
    }

    [Fact]
    public void IsNotModified_IfNoneMatchDifferent_ReturnsFalse()
    {
        var basket = NewBasket();
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.IfNoneMatch = "\"deadbeef\"";

        ETag.IsNotModified(ctx.Request, ETag.Compute(basket), basket.LastModifiedAt).Should().BeFalse();
    }

    [Fact]
    public void IsNotModified_IfModifiedSince_FutureCutoff_ReturnsTrue()
    {
        // Client says "I last saw it at noon UTC" — the basket's
        // LastModifiedAt is older than noon, so the client cache is
        // fresh and the response is 304.
        var basket = NewBasket();
        var now = SystemClock.Instance.GetCurrentInstant();
        var clientCutoff = now.Plus(Duration.FromHours(1)).ToDateTimeOffset().ToUniversalTime();
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.IfModifiedSince = clientCutoff.ToString("R");

        ETag.IsNotModified(ctx.Request, ETag.Compute(basket), now).Should().BeTrue();
    }

    [Fact]
    public void IsNotModified_NoHeaders_ReturnsFalse()
    {
        // No conditional request headers — the response is the
        // full 200 OK body.
        var basket = NewBasket();
        var ctx = new DefaultHttpContext();

        ETag.IsNotModified(ctx.Request, ETag.Compute(basket), basket.LastModifiedAt).Should().BeFalse();
    }

    private static Models.Basket NewBasket()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return new Models.Basket(Guid.NewGuid(), Guid.NewGuid())
        {
            CreatedAt = now,
            ExpiresAt = now.Plus(Duration.FromMinutes(30)),
            LastModifiedAt = now,
        };
    }
}
