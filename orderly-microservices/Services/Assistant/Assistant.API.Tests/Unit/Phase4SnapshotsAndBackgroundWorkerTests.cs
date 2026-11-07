using System.Security.Claims;
using Assistant.API.Domain.Entities;
using Assistant.API.Features.BackgroundJobs;
using Assistant.API.Features.Chat;
using Assistant.API.Features.Snapshots;
using Assistant.API.Infrastructure.Persistence;
using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Events.Ai;
using BuildingBlocks.Messaging.Events.Catalog;
using BuildingBlocks.Multitenancy;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;
using Xunit;

namespace Assistant.API.Tests.Unit;

public sealed class Phase4SnapshotsAndBackgroundWorkerTests
{
    private static readonly Instant FixedNow = Instant.FromUtc(2026, 11, 7, 20, 0, 0);

    [Fact]
    public async Task SnapshotConsumers_UpdateEightySixedItemsAndKitchenTicketCountsInDigest()
    {
        IClock clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(FixedNow);

        RestaurantContextSnapshotService snapshotService = new(clock);
        Guid restaurantId = Guid.NewGuid();

        // 1. Catalog MenuItemChangedIntegrationEvent marks "Ribeye Steak" unavailable
        MenuItemChangedSnapshotConsumer menuConsumer = new(snapshotService);
        ConsumeContext<MenuItemChangedIntegrationEvent> menuCtx = Substitute.For<ConsumeContext<MenuItemChangedIntegrationEvent>>();
        menuCtx.Message.Returns(new MenuItemChangedIntegrationEvent
        {
            RestaurantId = restaurantId,
            MenuItemId = Guid.NewGuid(),
            ChangeType = MenuItemChangeType.Updated,
            Name = "Ribeye Steak",
            IsAvailable = false,
            AvailabilityStatus = "Unavailable"
        });
        await menuConsumer.Consume(menuCtx);

        // 2. OrderCreatedIntegrationEvent increments active KDS tickets
        OrderCreatedSnapshotConsumer orderConsumer = new(snapshotService);
        ConsumeContext<OrderCreatedIntegrationEvent> orderCtx = Substitute.For<ConsumeContext<OrderCreatedIntegrationEvent>>();
        orderCtx.Message.Returns(new OrderCreatedIntegrationEvent
        {
            RestaurantId = restaurantId,
            OrderId = Guid.NewGuid(),
            OrderNumber = "ORD-101",
            Currency = "USD"
        });
        await orderConsumer.Consume(orderCtx);
        await orderConsumer.Consume(orderCtx);

        // 3. KitchenOrderBumpedIntegrationEvent decrements active KDS tickets
        KitchenOrderBumpedSnapshotConsumer bumpConsumer = new(snapshotService);
        ConsumeContext<KitchenOrderBumpedIntegrationEvent> bumpCtx = Substitute.For<ConsumeContext<KitchenOrderBumpedIntegrationEvent>>();
        bumpCtx.Message.Returns(new KitchenOrderBumpedIntegrationEvent
        {
            RestaurantId = restaurantId,
            OrderId = Guid.NewGuid(),
            BumpedByUserId = Guid.NewGuid(),
            BumpedAt = FixedNow
        });
        await bumpConsumer.Consume(bumpCtx);

        RestaurantOperationalSnapshotDto snapshot = await snapshotService.GetSnapshotAsync(restaurantId);
        snapshot.EightySixedItems.Should().ContainSingle().Which.Should().Be("Ribeye Steak");
        snapshot.ActiveKitchenTickets.Should().Be(1);
        snapshot.ToCompactSystemPromptDigest().Should().Contain("Ribeye Steak");
    }

    [Fact]
    public async Task AiUsageLedgerConsumer_PersistsUsageEventIdempotentlyOnDuplicateDelivery()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid usageRecordId = Guid.NewGuid();

        DbContextOptions<AiDbContext> dbOptions = new DbContextOptionsBuilder<AiDbContext>()
            .UseInMemoryDatabase($"ai-ledger-consumer-{Guid.NewGuid()}")
            .Options;

        StubTenantProvider tenantProvider = new(restaurantId);
        using AiDbContext dbContext = new(dbOptions, tenantProvider);

        AiUsageLedgerConsumer consumer = new(
            dbContext,
            tenantProvider,
            NullLogger<AiUsageLedgerConsumer>.Instance);

        AiUsageRecordedIntegrationEvent evt = new()
        {
            UsageRecordId = usageRecordId,
            RestaurantId = restaurantId,
            UserId = Guid.NewGuid(),
            ConversationId = "conv-99",
            OperationType = "FastChat",
            Provider = "Gemini",
            Model = "gemini-2.5-flash-lite",
            PromptTokens = 400,
            CachedTokens = 200,
            CompletionTokens = 100,
            EffectiveBillableTokens = 550,
            EstimatedCostUsd = 0.0002m,
            BillingCycle = "2026-11",
            WasOverage = false,
            RecordedAt = FixedNow
        };

        ConsumeContext<AiUsageRecordedIntegrationEvent> consumeContext = Substitute.For<ConsumeContext<AiUsageRecordedIntegrationEvent>>();
        consumeContext.Message.Returns(evt);

        // Deliver twice to verify idempotency
        await consumer.Consume(consumeContext);
        await consumer.Consume(consumeContext);

        List<AiUsageLedger> rows = await dbContext.UsageLedgers.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(usageRecordId);
        rows[0].EffectiveBillableTokens.Should().Be(550);
    }

    [Fact]
    public async Task AiBackgroundWorker_ExecutesDetachedJobAndBroadcastsProgressAndCompletionOverSignalR()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid jobId = Guid.NewGuid();
        Guid reservationId = Guid.NewGuid();

        IAiAssistantOrchestrator orchestrator = Substitute.For<IAiAssistantOrchestrator>();
        orchestrator.ExecuteTurnAsync(Arg.Any<AiChatTurnRequest>(), reservationId, Arg.Any<CancellationToken>())
            .Returns(new AiOrchestrationSummary(
                ConversationId: "conv-bg-1",
                SelectedModel: TieredAiOrchestrator.Tier2DeepModel,
                OperationType: "DeepAnalyticsJob",
                ResponseText: "### Gross Margin Audit\nTop seller: Wagyu Smash Burger (74.2% margin).",
                Proposal: null,
                InjectedPluginNames: ["MarginAnalyticsPlugin"],
                InvokedReadTools: ["get_margin_analytics"],
                ReadToolIterations: 1,
                UncachedPromptTokens: 600,
                CachedPromptTokens: 400,
                CompletionTokens: 250,
                EffectiveBillableTokens: 950,
                WasOverage: false));

        ServiceCollection services = new();
        services.AddScoped<ICurrentRestaurantProvider>(_ => new StubTenantProvider(restaurantId));
        services.AddScoped<IAiAssistantOrchestrator>(_ => orchestrator);
        using ServiceProvider provider = services.BuildServiceProvider();

        IAssistantHubClient groupClient = Substitute.For<IAssistantHubClient>();
        IHubClients<IAssistantHubClient> hubClients = Substitute.For<IHubClients<IAssistantHubClient>>();
        hubClients.Group(Arg.Any<string>()).Returns(groupClient);

        IHubContext<AssistantHub, IAssistantHubClient> hubContext = Substitute.For<IHubContext<AssistantHub, IAssistantHubClient>>();
        hubContext.Clients.Returns(hubClients);

        IClock clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(FixedNow);

        using AiBackgroundWorker worker = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            hubContext,
            clock,
            NullLogger<AiBackgroundWorker>.Instance);

        AiBackgroundJob job = new(
            JobId: jobId,
            RestaurantId: restaurantId,
            UserId: userId,
            ConversationId: "conv-bg-1",
            Prompt: "What are today's top 5 gross margin sellers?",
            JobType: "DeepAnalyticsJob",
            ReservationId: reservationId,
            CallerRoles: ["Manager"],
            RequestedAt: FixedNow);

        AiBackgroundJobState completed = await worker.ProcessJobNowAsync(job);

        completed.Status.Should().Be("Completed");
        completed.ProgressPercent.Should().Be(100);
        completed.EffectiveTokensUsed.Should().Be(950);
        completed.ResultMarkdown.Should().Contain("Wagyu Smash Burger");

        await groupClient.Received().AiJobProgress(Arg.Is<AiJobProgressMessage>(m => m.JobId == jobId && m.Status == "Running"));
        await groupClient.Received().AiJobCompleted(Arg.Is<AiJobCompletedMessage>(m => m.JobId == jobId && m.Status == "Completed" && m.EffectiveTokensUsed == 950));
    }

    private sealed class StubTenantProvider(Guid initialRestaurantId) : ICurrentRestaurantProvider
    {
        public Guid RestaurantId { get; private set; } = initialRestaurantId;

        public IDisposable Attach(ClaimsPrincipal principal)
        {
            string? claimVal = principal.FindFirst("restaurantId")?.Value;
            if (Guid.TryParse(claimVal, out Guid parsed))
            {
                RestaurantId = parsed;
            }

            return new NoopScope();
        }

        private sealed class NoopScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
