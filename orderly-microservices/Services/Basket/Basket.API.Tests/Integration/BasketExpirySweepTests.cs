using Basket.API.Services;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodaTime;

namespace Basket.API.Tests.Integration;

/// <summary>
/// Phase 5.1 Commit 2 — Marten-fan-out assertions for
/// <see cref="BasketExpirySweepService"/> against the real Postgres
/// Testcontainer. Locks the §6 Phase 3 drift item 3 contract: an
/// expired basket is deleted, a live basket is untouched.
/// </summary>
/// <remarks>
/// <para>The two <see cref="BasketExpirySweepServiceTests"/> unit
/// tests cover the lifecycle surface (Enabled short-circuit +
/// cancellation propagation); the Marten <c>IMartenQueryable&lt;T&gt;</c>
/// query chain is not mockable via NSubstitute, so the actual
/// deletion logic is exercised here against a real Postgres
/// container.</para>
/// <para>The fixture's hosted service is left enabled
/// (<see cref="BasketExpirySweepWebApplicationFactory"/> sets
/// <c>Basket:ExpirySweep:Enabled=true</c>) so the
/// integration test mirrors the production deployment shape, but
/// each test invokes <see cref="BasketExpirySweepService.SweepOnceAsync"/>
/// directly rather than waiting on the periodic timer. The return
/// count + the post-call Marten state are the authoritative
/// assertions, not the wall-clock timing of the background tick —
/// the deep past margin on <see cref="BasketSeedHelper.SeedExpiredBasketAsync"/>
/// (default <c>now - 1h</c>) makes the assertions deterministic
/// whether the background tick has already fired or not.</para>
/// <para>Tenant scoping mirrors
/// <see cref="BasketSeedHelper.SeedBasketAsync"/>: outside an HTTP
/// request the ambient <c>ClaimsRestaurantProvider</c> returns
/// <see cref="Guid.Empty"/>, so the test's read-back session uses
/// the explicit-tenant-id <c>IDocumentStore.LightweightSession(TestRestaurantId)</c>
/// pattern. The <see cref="BasketSeedHelper.TestRestaurantId"/>
/// constant is the same value <see cref="TestAuthHandler"/> stamps
/// on the JWT, so any follow-up endpoint call sees the same tenant
/// partition.</para>
/// </remarks>
[Collection(nameof(BasketExpirySweepWebApplicationFactoryCollection))]
public sealed class BasketExpirySweepTests(BasketExpirySweepWebApplicationFactory factory)
{
    [Fact]
    public async Task ExpiredBasket_Deleted()
    {
        // Arrange — seed an expired basket (ExpiresAt = now - 1h by default).
        var userId = Guid.NewGuid();
        await factory.SeedExpiredBasketAsync(userId);

        // Act — drive the sweep directly via the public test surface.
        var sweep = GetSweepService();
        var deleted = await sweep.SweepOnceAsync(CancellationToken.None);

        // Assert — the sweep reported exactly one deletion AND the row
        // is gone from the tenant partition. Asserting on both
        // guards against a future regression where the count comes
        // back correct but the row lingers (or vice versa).
        deleted.Should().Be(1,
            "exactly one expired basket was seeded, and the sweep should report the deletion count");

        var remaining = await LoadSweepTenantBasketAsync(userId);
        remaining.Should().BeEmpty(
            "the expired basket must be deleted from the Marten store, not just hidden from the projection");
    }

    [Fact]
    public async Task LiveBasket_NotTouched()
    {
        // Arrange — seed a live basket (ExpiresAt = now + 1h) into the
        // sweep-visible DEFAULT tenant partition.
        var userId = Guid.NewGuid();
        await factory.SeedBasketInSweepTenantAsync(userId, b =>
        {
            b.ExpiresAt = SystemClock.Instance.GetCurrentInstant() + Duration.FromHours(1);
        });

        // Act
        var sweep = GetSweepService();
        var deleted = await sweep.SweepOnceAsync(CancellationToken.None);

        // Assert — sweep skipped the live basket; count is 0, row remains.
        deleted.Should().Be(0,
            "the seeded basket's ExpiresAt is in the future, so the sweep must skip it");

        var remaining = await LoadSweepTenantBasketAsync(userId);
        remaining.Should().ContainSingle()
            .Which.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task MixedExpiredAndLive_DeletesOnlyExpired()
    {
        // Arrange — one expired + one live basket (different users so
        // they don't collide in the DEFAULT tenant partition).
        var expiredUserId = Guid.NewGuid();
        var liveUserId = Guid.NewGuid();
        await factory.SeedExpiredBasketAsync(expiredUserId);
        await factory.SeedBasketInSweepTenantAsync(liveUserId, b =>
        {
            b.ExpiresAt = SystemClock.Instance.GetCurrentInstant() + Duration.FromHours(1);
        });

        // Act
        var sweep = GetSweepService();
        var deleted = await sweep.SweepOnceAsync(CancellationToken.None);

        // Assert — count == 1 (only the expired one), expired row gone, live row present.
        deleted.Should().Be(1,
            "the sweep must report exactly one deletion in a mixed batch");

        (await LoadSweepTenantBasketAsync(expiredUserId)).Should().BeEmpty(
            "the expired basket must be deleted");

        (await LoadSweepTenantBasketAsync(liveUserId)).Should().ContainSingle()
            .Which.UserId.Should().Be(liveUserId,
                "the live basket must be preserved untouched");
    }

    [Fact]
    public async Task EmptyStore_ReturnsZero()
    {
        // Arrange — no seeds.

        // Act — the sweep runs against an empty tenant partition.
        var sweep = GetSweepService();
        var deleted = await sweep.SweepOnceAsync(CancellationToken.None);

        // Assert — the sweep returned 0 without throwing.
        deleted.Should().Be(0,
            "the sweep should report zero deletions when no expired baskets exist");
    }

    /// <summary>
    /// Opens a non-tenant-scoped Marten session (matches the sweep
    /// service's query path — see <see cref="BasketSeedHelper.SeedExpiredBasketAsync"/>
    /// xmldoc) and returns the basket matching <paramref name="userId"/>.
    /// </summary>
    private async Task<IReadOnlyList<Models.Basket>> LoadSweepTenantBasketAsync(Guid userId)
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        // NO tenant id — the sweep service queries without one, so the
        // verification session must read from the same DEFAULT partition.
        await using var session = store.LightweightSession();

        return await session.Query<Models.Basket>()
            .Where(b => b.UserId == userId)
            .ToListAsync();
    }

    /// <summary>
    /// Resolves the <see cref="BasketExpirySweepService"/> from the
    /// hosted-service collection. The service is registered as
    /// <c>AddHostedService&lt;BasketExpirySweepService&gt;</c> in
    /// <c>Basket.API/Program.cs:237</c> — only resolvable as
    /// <see cref="IHostedService"/>, not as the concrete type. The
    /// test fixture shares the WAF's service collection (the sweep
    /// service is the same singleton that <c>ExecuteAsync</c> drives
    /// on the background thread), so calling
    /// <see cref="BasketExpirySweepService.SweepOnceAsync"/> here
    /// exercises the same code path the production deployment runs.
    /// </summary>
    private BasketExpirySweepService GetSweepService()
    {
        var hosted = factory.Services
            .GetServices<IHostedService>()
            .OfType<BasketExpirySweepService>()
            .Single();
        return hosted;
    }
}