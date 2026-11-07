using Assistant.API.Domain.Entities;
using Assistant.API.Infrastructure.Metering;
using FluentAssertions;
using NodaTime;
using Xunit;

namespace Assistant.API.Tests.Unit;

public sealed class RedisQuotaEngineTests
{
    [Fact]
    public void EmbeddedLuaScripts_ContainZsetSelfHealingEvictionAndQuarterCachedTokenWeighting()
    {
        string reserveLua = RedisQuotaEngine.GetReserveLuaScript();
        string settleLua = RedisQuotaEngine.GetSettleLuaScript();

        reserveLua.Should().Contain("ZRANGEBYSCORE");
        reserveLua.Should().Contain("ZREMRANGEBYSCORE");
        reserveLua.Should().Contain("HARD_CAP_EXCEEDED");
        reserveLua.Should().Contain("SAFETY_CEILING_EXCEEDED");

        settleLua.Should().Contain("math.ceil(cachedPrompt * 0.25)");
        settleLua.Should().Contain("ZREM");
        settleLua.Should().Contain("HDEL");
    }

    [Fact]
    public async Task ReserveQuotaAsync_Under50ConcurrentRequests_NeverExceedsHardCap()
    {
        FakeManualClock clock = new(Instant.FromUtc(2026, 11, 7, 14, 0, 0));
        // Base quota: 25,000 tokens; 50 concurrent threads each requesting 1,000 tokens -> exactly 25 should succeed
        RedisQuotaEngine engine = new(clock, redis: null, defaultBaseQuota: 25_000);
        Guid restaurantId = Guid.NewGuid();

        Task<QuotaReservationResult>[] tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => engine.ReserveQuotaAsync(restaurantId, estimatedTokens: 1_000)))
            .ToArray();

        QuotaReservationResult[] results = await Task.WhenAll(tasks);

        int allowedCount = results.Count(r => r.Allowed);
        int rejectedCount = results.Count(r => !r.Allowed && r.IsHardCapExceeded);

        allowedCount.Should().Be(25);
        rejectedCount.Should().Be(25);

        TenantQuotaStatusDto status = await engine.GetQuotaStatusAsync(restaurantId);
        status.ActiveReservedTokens.Should().Be(25_000);
        status.StatusTier.Should().Be("capped");
    }

    [Fact]
    public async Task ReserveQuotaAsync_WhenMidStreamCrashLeavesOrphanedHolds_SelfHealsAfterTtlExpiry()
    {
        FakeManualClock clock = new(Instant.FromUtc(2026, 11, 7, 15, 0, 0));
        RedisQuotaEngine engine = new(clock, redis: null, defaultBaseQuota: 10_000);
        Guid restaurantId = Guid.NewGuid();

        // Reserve full 10,000 tokens with a 2-minute TTL and simulate node crash (never settled)
        QuotaReservationResult firstHold = await engine.ReserveQuotaAsync(
            restaurantId,
            estimatedTokens: 10_000,
            reservationTtl: Duration.FromMinutes(2));
        firstHold.Allowed.Should().BeTrue();

        // Immediate second request before TTL expires -> rejected with HARD_CAP_EXCEEDED
        QuotaReservationResult blockedAttempt = await engine.ReserveQuotaAsync(
            restaurantId,
            estimatedTokens: 1_000);
        blockedAttempt.Allowed.Should().BeFalse();
        blockedAttempt.StatusReason.Should().Be("HARD_CAP_EXCEEDED");

        // Advance clock past the 2-minute ZSET TTL
        clock.Advance(Duration.FromSeconds(121));

        // Next reservation automatically evicts the orphaned ZSET hold and succeeds
        QuotaReservationResult healedAttempt = await engine.ReserveQuotaAsync(
            restaurantId,
            estimatedTokens: 4_000);
        healedAttempt.Allowed.Should().BeTrue();
        healedAttempt.CommittedTokens.Should().Be(4_000);
    }

    [Fact]
    public async Task SettleQuotaAsync_AppliesQuarterWeightingToCachedTokensAndReleasesHold()
    {
        FakeManualClock clock = new(Instant.FromUtc(2026, 11, 7, 16, 0, 0));
        RedisQuotaEngine engine = new(clock, redis: null, defaultBaseQuota: 250_000);
        Guid restaurantId = Guid.NewGuid();

        QuotaReservationResult reservation = await engine.ReserveQuotaAsync(restaurantId, estimatedTokens: 2_000);
        reservation.Allowed.Should().BeTrue();

        // Settle: 500 uncached + 800 cached (800 * 0.25 = 200) + 300 completion = 1,000 effective tokens
        QuotaSettlementResult settlement = await engine.SettleQuotaAsync(
            restaurantId,
            reservation.ReservationId,
            uncachedPromptTokens: 500,
            cachedPromptTokens: 800,
            completionTokens: 300,
            model: "gemini-2.5-flash-lite");

        settlement.EffectiveDeltaTokens.Should().Be(1_000);
        settlement.TotalEffectiveUsedTokens.Should().Be(1_000);
        settlement.WasOverage.Should().BeFalse();

        TenantQuotaStatusDto status = await engine.GetQuotaStatusAsync(restaurantId);
        status.UsedEffectiveTokens.Should().Be(1_000);
        status.ActiveReservedTokens.Should().Be(0);
    }

    [Fact]
    public async Task ReserveQuotaAsync_WithOptInSafetyCeiling_AllowsOverageUpToSafetyCeilingThenBlocks()
    {
        FakeManualClock clock = new(Instant.FromUtc(2026, 11, 7, 17, 0, 0));
        RedisQuotaEngine engine = new(clock, redis: null, defaultBaseQuota: 10_000);
        Guid restaurantId = Guid.NewGuid();

        RestaurantAiQuotaConfig config = RestaurantAiQuotaConfig.CreateDefault(restaurantId, clock.GetCurrentInstant());
        config.IncludedTokensPerCycle = 10_000;
        config.UpdateSafetyCeiling(
            allowOverage: true,
            maxOverageTokens: 5_000,
            maxOverageSpendUsd: 5.00m,
            autoPurchasePacksEnabled: true,
            actor: "admin",
            now: clock.GetCurrentInstant());

        await engine.SyncTenantConfigAsync(config);

        // Request 12,000 tokens (exceeds 10k base, within 15k safety ceiling) -> Allowed
        QuotaReservationResult overageRes = await engine.ReserveQuotaAsync(restaurantId, estimatedTokens: 12_000);
        overageRes.Allowed.Should().BeTrue();
        overageRes.LimitTokens.Should().Be(15_000);

        // Request another 4,000 tokens (16,000 > 15,000 ceiling) -> SAFETY_CEILING_EXCEEDED
        QuotaReservationResult ceilingExceeded = await engine.ReserveQuotaAsync(restaurantId, estimatedTokens: 4_000);
        ceilingExceeded.Allowed.Should().BeFalse();
        ceilingExceeded.IsSafetyCeilingExceeded.Should().BeTrue();
        ceilingExceeded.StatusReason.Should().Be("SAFETY_CEILING_EXCEEDED");
    }

    private sealed class FakeManualClock(Instant initial) : IClock
    {
        private long _unixMs = initial.ToUnixTimeMilliseconds();

        public Instant GetCurrentInstant() => Instant.FromUnixTimeMilliseconds(Interlocked.Read(ref _unixMs));

        public void Advance(Duration delta) => Interlocked.Add(ref _unixMs, (long)delta.TotalMilliseconds);
    }
}
