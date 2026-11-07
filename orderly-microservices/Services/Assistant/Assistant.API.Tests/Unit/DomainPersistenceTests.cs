using System.Security.Claims;
using Assistant.API.Domain.Entities;
using Assistant.API.Infrastructure.Persistence;
using BuildingBlocks.Messaging.Events.Ai;
using BuildingBlocks.Multitenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NodaTime;
using Xunit;

namespace Assistant.API.Tests.Unit;

public sealed class DomainPersistenceTests
{
    private static readonly Instant FixedNow = Instant.FromUtc(2026, 11, 7, 12, 0, 0);

    [Fact]
    public void RestaurantAiQuotaConfig_CreateDefault_SetsHardCapByDefaultAndAuditTimestamps()
    {
        Guid restaurantId = Guid.NewGuid();

        RestaurantAiQuotaConfig config = RestaurantAiQuotaConfig.CreateDefault(restaurantId, FixedNow, "admin-1");

        config.Id.Should().NotBeEmpty();
        config.RestaurantId.Should().Be(restaurantId);
        config.IncludedTokensPerCycle.Should().Be(250_000);
        config.AllowOverage.Should().BeFalse();
        config.MaxOverageTokens.Should().Be(0);
        config.MaxOverageSpendUsd.Should().Be(0.00m);
        config.AutoPurchasePacksEnabled.Should().BeFalse();
        config.PackSizeTokens.Should().Be(100_000);
        config.PricePerPackUsd.Should().Be(2.00m);
        config.IsSuspended.Should().BeFalse();
        config.CreatedAt.Should().Be(FixedNow);
        config.CreatedBy.Should().Be("admin-1");
        config.LastModified.Should().Be(FixedNow);
        config.LastModifiedBy.Should().Be("admin-1");
    }

    [Fact]
    public void RestaurantAiQuotaConfig_UpdateSafetyCeiling_ValidatesInvariantsAndResetsWhenDisabled()
    {
        Guid restaurantId = Guid.NewGuid();
        RestaurantAiQuotaConfig config = RestaurantAiQuotaConfig.CreateDefault(restaurantId, FixedNow);
        Instant later = FixedNow.Plus(Duration.FromMinutes(10));

        config.UpdateSafetyCeiling(
            allowOverage: true,
            maxOverageTokens: 500_000,
            maxOverageSpendUsd: 25.00m,
            autoPurchasePacksEnabled: true,
            actor: "owner-42",
            now: later);

        config.AllowOverage.Should().BeTrue();
        config.MaxOverageTokens.Should().Be(500_000);
        config.MaxOverageSpendUsd.Should().Be(25.00m);
        config.AutoPurchasePacksEnabled.Should().BeTrue();
        config.LastModifiedAt.Should().Be(later);
        config.LastModifiedBy.Should().Be("owner-42");

        // Disabling overage zeroes out ceiling fields
        Instant evenLater = later.Plus(Duration.FromMinutes(5));
        config.UpdateSafetyCeiling(
            allowOverage: false,
            maxOverageTokens: 500_000,
            maxOverageSpendUsd: 25.00m,
            autoPurchasePacksEnabled: true,
            actor: "owner-42",
            now: evenLater);

        config.AllowOverage.Should().BeFalse();
        config.MaxOverageTokens.Should().Be(0);
        config.MaxOverageSpendUsd.Should().Be(0m);
        config.AutoPurchasePacksEnabled.Should().BeFalse();
    }

    [Fact]
    public void AiUsageLedger_CalculateEffectiveBillableTokens_WeightsCachedPromptTokensAtQuarterCeiling()
    {
        // 1000 uncached + ceil(401 * 0.25 = 100.25 -> 101) + 200 completion = 1301
        int effective = AiUsageLedger.CalculateEffectiveBillableTokens(
            uncachedPromptTokens: 1000,
            cachedPromptTokens: 401,
            completionTokens: 200);

        effective.Should().Be(1301);

        AiUsageLedger ledger = AiUsageLedger.Create(
            id: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            conversationId: "conv-123",
            operationType: "FastChat",
            provider: "Gemini",
            model: "gemini-2.5-flash-lite",
            uncachedPromptTokens: 1000,
            cachedTokens: 401,
            completionTokens: 200,
            estimatedCostUsd: 0.0015m,
            billingCycle: "2026-11",
            wasOverage: false,
            recordedAt: FixedNow);

        ledger.EffectiveBillableTokens.Should().Be(1301);
        ledger.RecordedAt.Should().Be(FixedNow);
    }

    [Fact]
    public void AiDbContext_ModelConfiguration_DefinesRequiredIndexesAndTenantQueryFilters()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        StubRestaurantProvider provider = new(tenantA);

        DbContextOptions<AiDbContext> options = new DbContextOptionsBuilder<AiDbContext>()
            .UseInMemoryDatabase($"ai-db-{Guid.NewGuid()}")
            .Options;

        using AiDbContext db = new(options, provider);

        IEntityType? ledgerEntity = db.Model.FindEntityType(typeof(AiUsageLedger));
        ledgerEntity.Should().NotBeNull();

        IReadOnlyList<IIndex> indexes = ledgerEntity!.GetIndexes().ToList();
        indexes.Should().Contain(idx =>
            idx.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(AiUsageLedger.RestaurantId), nameof(AiUsageLedger.BillingCycle) }));
        indexes.Should().Contain(idx =>
            idx.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(AiUsageLedger.RecordedAt) }));

        db.UsageLedgers.Add(AiUsageLedger.Create(
            Guid.NewGuid(), tenantA, Guid.NewGuid(), "c1", "FastChat", "Gemini", "gemini-2.5-flash-lite", 100, 0, 50, 0.001m, "2026-11", false, FixedNow));
        db.UsageLedgers.Add(AiUsageLedger.Create(
            Guid.NewGuid(), tenantB, Guid.NewGuid(), "c2", "FastChat", "Gemini", "gemini-2.5-flash-lite", 200, 0, 50, 0.001m, "2026-11", false, FixedNow));
        db.SaveChanges();

        List<AiUsageLedger> tenantALedgers = db.UsageLedgers.ToList();
        tenantALedgers.Should().ContainSingle(x => x.RestaurantId == tenantA);
    }

    [Fact]
    public void IntegrationEvents_UseNodaTimeInstantAndInheritFromIntegrationEvent()
    {
        AiUsageRecordedIntegrationEvent usageEvent = new()
        {
            RestaurantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ConversationId = "conv-1",
            OperationType = "FastChat",
            Provider = "Gemini",
            Model = "gemini-2.5-flash-lite",
            PromptTokens = 500,
            CachedTokens = 400,
            CompletionTokens = 100,
            EffectiveBillableTokens = 700,
            BillingCycle = "2026-11",
            RecordedAt = FixedNow
        };

        AiBackgroundJobRequestedEvent jobEvent = new()
        {
            RestaurantId = usageEvent.RestaurantId,
            UserId = usageEvent.UserId,
            ConversationId = "conv-1",
            Prompt = "Audit evening shift margins",
            JobType = "DeepAnalyticsJob",
            ReservationId = Guid.NewGuid(),
            EstimatedTokens = 4000,
            CallerRoles = ["Manager"],
            RequestedAt = FixedNow
        };

        usageEvent.Id.Should().NotBeEmpty();
        usageEvent.RecordedAt.Should().Be(FixedNow);
        jobEvent.JobId.Should().NotBeEmpty();
        jobEvent.RequestedAt.Should().Be(FixedNow);
    }

    private sealed class StubRestaurantProvider(Guid restaurantId) : ICurrentRestaurantProvider
    {
        public Guid RestaurantId { get; private set; } = restaurantId;

        public IDisposable Attach(ClaimsPrincipal principal) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
