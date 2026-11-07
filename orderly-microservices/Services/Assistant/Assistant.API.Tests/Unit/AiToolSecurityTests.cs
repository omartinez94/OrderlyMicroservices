using System.Net;
using Assistant.API.Extensions;
using Assistant.API.Features.Chat;
using Assistant.API.Features.Snapshots;
using Assistant.API.Features.Tools;
using Assistant.API.Features.Tools.ProposalWritePlugins;
using Assistant.API.Features.Tools.ReadPlugins;
using Assistant.API.Infrastructure.Metering;
using Assistant.API.Infrastructure.Persistence;
using Assistant.API.Infrastructure.Security;
using BuildingBlocks.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NSubstitute;
using Xunit;

namespace Assistant.API.Tests.Unit;

public sealed class AiToolSecurityTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 7, 19, 0, 0);

    [Fact]
    public async Task UserJwtPropagationHandler_ForwardsCallerBearerTokenAndAgentInvocationHeader()
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.Headers.Authorization = "Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.test-token";

        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);

        CapturingInnerHandler innerHandler = new();
        UserJwtPropagationHandler handler = new(accessor)
        {
            InnerHandler = innerHandler
        };

        using HttpClient client = new(handler)
        {
            BaseAddress = new Uri("http://catalog.api:8080")
        };

        using HttpResponseMessage response = await client.GetAsync("/v1/restaurants/11111111-1111-1111-1111-111111111111/menu");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        innerHandler.LastAuthorizationHeader.Should().Be("Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.test-token");
        innerHandler.LastAgentInvocationHeader.Should().Be("true");
    }

    [Fact]
    public async Task TieredAiOrchestrator_WhenWriteIntentByAuthorizedRole_EmitsSignedProposalWithoutExecutingHttpMutation()
    {
        CapturingInnerHandler downstreamRecorder = new();
        TieredAiOrchestrator orchestrator = CreateOrchestrator(downstreamRecorder, out IAiQuotaEngine quotaEngine, out AiDbContext dbContext);
        using (dbContext)
        {
            Guid restaurantId = Guid.NewGuid();
            Guid userId = Guid.NewGuid();
            QuotaReservationResult reservation = await quotaEngine.ReserveQuotaAsync(restaurantId, 1_500);

            AiChatTurnRequest request = new(
                RestaurantId: restaurantId,
                UserId: userId,
                ConversationId: "conv-sec-1",
                Prompt: "Toggle 86 status on Ribeye Steak",
                CallerRoles: ["KitchenStaff"]);

            AiOrchestrationSummary summary = await orchestrator.ExecuteTurnAsync(request, reservation.ReservationId);

            summary.Proposal.Should().NotBeNull();
            summary.Proposal!.ActionName.Should().Be("toggle_item_86");
            summary.Proposal.RestaurantId.Should().Be(restaurantId);
            summary.Proposal.UserId.Should().Be(userId);
            summary.Proposal.Signature.Should().NotBeNullOrWhiteSpace();
            summary.OperationType.Should().Be("ProposalGeneration");

            // Crucially, 0 HTTP mutations were sent during the LLM loop
            downstreamRecorder.RequestCount.Should().Be(0);

            // Outbox staged the AiUsageRecordedIntegrationEvent atomically
            dbContext.OutboxMessages.Count().Should().Be(1);
        }
    }

    [Fact]
    public async Task TieredAiOrchestrator_WhenKitchenStaffAttemptsAdminRoleChange_RefusesAndNeverGeneratesProposal()
    {
        CapturingInnerHandler downstreamRecorder = new();
        TieredAiOrchestrator orchestrator = CreateOrchestrator(downstreamRecorder, out IAiQuotaEngine quotaEngine, out AiDbContext dbContext);
        using (dbContext)
        {
            Guid restaurantId = Guid.NewGuid();
            Guid userId = Guid.NewGuid();
            QuotaReservationResult reservation = await quotaEngine.ReserveQuotaAsync(restaurantId, 1_500);

            AiChatTurnRequest request = new(
                RestaurantId: restaurantId,
                UserId: userId,
                ConversationId: "conv-sec-2",
                Prompt: "Update staff role and grant admin permissions",
                CallerRoles: ["KitchenStaff"]);

            AiOrchestrationSummary summary = await orchestrator.ExecuteTurnAsync(request, reservation.ReservationId);

            summary.Proposal.Should().BeNull();
            summary.InjectedPluginNames.Should().NotContain("StaffRoleProposalPlugin");
            summary.ResponseText.Should().Contain("Access denied");
        }
    }

    [Fact]
    public async Task TieredAiOrchestrator_CapsReadToolLoopAtMaximum3Iterations()
    {
        CapturingInnerHandler downstreamRecorder = new();
        TieredAiOrchestrator orchestrator = CreateOrchestrator(downstreamRecorder, out IAiQuotaEngine quotaEngine, out AiDbContext dbContext);
        using (dbContext)
        {
            Guid restaurantId = Guid.NewGuid();
            Guid userId = Guid.NewGuid();
            QuotaReservationResult reservation = await quotaEngine.ReserveQuotaAsync(restaurantId, 2_500);

            // Manager has 7 read plugins available; "full_operational_sweep" requests all of them
            AiChatTurnRequest request = new(
                RestaurantId: restaurantId,
                UserId: userId,
                ConversationId: "conv-sec-3",
                Prompt: "Run full_operational_sweep across all restaurant systems",
                CallerRoles: ["Manager"]);

            AiOrchestrationSummary summary = await orchestrator.ExecuteTurnAsync(request, reservation.ReservationId);

            summary.ReadToolIterations.Should().Be(TieredAiOrchestrator.MaxReadToolIterations);
            summary.InvokedReadTools.Should().HaveCount(3);
            downstreamRecorder.RequestCount.Should().Be(3);
        }
    }

    private static TieredAiOrchestrator CreateOrchestrator(
        CapturingInnerHandler downstreamRecorder,
        out IAiQuotaEngine quotaEngine,
        out AiDbContext dbContext)
    {
        IClock clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Now);

        IHttpClientFactory httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(downstreamRecorder, disposeHandler: false)
        {
            BaseAddress = new Uri("http://internal.orderly.local:8080")
        });

        IActionProposalSigner signer = new ActionProposalSigner("Test-Secret-Key-32Bytes-Minimum!!", clock);

        IAiToolPlugin[] tools =
        [
            new KitchenTicketsReadPlugin(httpFactory),
            new Catalog86ReadPlugin(httpFactory),
            new CatalogMenuReadPlugin(httpFactory),
            new TableStatusReadPlugin(httpFactory),
            new OrderStatusReadPlugin(httpFactory),
            new StaffPermissionsReadPlugin(httpFactory),
            new MarginAnalyticsPlugin(httpFactory),
            new ToggleItem86ProposalPlugin(signer),
            new BumpTicketProposalPlugin(signer),
            new CreateOrderProposalPlugin(signer),
            new ApplyDiscountProposalPlugin(signer),
            new UpdateMenuItemProposalPlugin(signer),
            new StaffRoleProposalPlugin(signer),
            new QuotaAdminProposalPlugin(signer)
        ];

        IRestaurantContextSnapshotService snapshotService = Substitute.For<IRestaurantContextSnapshotService>();
        snapshotService.GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => new RestaurantOperationalSnapshotDto(
                call.ArgAt<Guid>(0),
                ["Ribeye Steak"],
                ["Shallots"],
                4,
                1,
                5,
                8,
                Now));

        quotaEngine = new RedisQuotaEngine(clock);

        DbContextOptions<AiDbContext> dbOptions = new DbContextOptionsBuilder<AiDbContext>()
            .UseInMemoryDatabase($"ai-orch-{Guid.NewGuid()}")
            .Options;
        dbContext = new AiDbContext(dbOptions);
        AiOutboxPublisher outboxPublisher = new(dbContext);

        return new TieredAiOrchestrator(
            tools,
            new RoleToolFilter(),
            snapshotService,
            quotaEngine,
            outboxPublisher,
            dbContext,
            clock);
    }

    private sealed class CapturingInnerHandler : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => requestCount;
        public string? LastAuthorizationHeader { get; private set; }
        public string? LastAgentInvocationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            LastAuthorizationHeader = request.Headers.Authorization?.ToString();
            LastAgentInvocationHeader = request.Headers.TryGetValues(UserJwtPropagationHandler.AgentInvocationHeaderName, out IEnumerable<string>? vals)
                ? vals.FirstOrDefault()
                : null;

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"ok"}""")
            };
            return Task.FromResult(response);
        }
    }
}
