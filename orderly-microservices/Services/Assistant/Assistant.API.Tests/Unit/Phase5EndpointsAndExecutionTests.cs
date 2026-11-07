using System.Net;
using System.Security.Claims;
using System.Text;
using Assistant.API.Domain.Entities;
using Assistant.API.Extensions;
using Assistant.API.Features.BackgroundJobs;
using Assistant.API.Features.Chat;
using Assistant.API.Features.Quota;
using Assistant.API.Features.Snapshots;
using Assistant.API.Features.Tools;
using Assistant.API.Features.Tools.ProposalWritePlugins;
using Assistant.API.Features.Tools.ReadPlugins;
using Assistant.API.Infrastructure.Metering;
using Assistant.API.Infrastructure.Persistence;
using Assistant.API.Infrastructure.Security;
using BuildingBlocks.Multitenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;
using Xunit;

namespace Assistant.API.Tests.Unit;

public sealed class Phase5EndpointsAndExecutionTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 7, 21, 0, 0);

    [Fact]
    public async Task StreamChatEndpoint_WhenHardCapExhausted_Returns429Rfc7807ProblemDetailsWithHardCapErrorCode()
    {
        IClock clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Now);

        // Configure engine with 1,000 token hard cap and consume it
        RedisQuotaEngine quotaEngine = new(clock, redis: null, defaultBaseQuota: 1_000);
        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        await quotaEngine.ReserveQuotaAsync(restaurantId, 1_000);

        DefaultHttpContext httpContext = CreateAuthenticatedHttpContext(restaurantId, userId, ["Manager"]);
        StubRestaurantProvider restaurantProvider = new(restaurantId);
        IAiAssistantOrchestrator orchestrator = Substitute.For<IAiAssistantOrchestrator>();

        StreamChatRequestDto request = new(
            RestaurantId: restaurantId,
            ConversationId: "conv-429",
            Prompt: "Show all 86'd items",
            EstimatedTokens: 500);

        IResult result = await StreamChatEndpoint.HandleStreamChatAsync(
            request,
            httpContext,
            restaurantProvider,
            quotaEngine,
            orchestrator,
            CancellationToken.None);

        ProblemHttpResult problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        problemResult.ProblemDetails.Title.Should().Be("Monthly AI Quota Exceeded");
        problemResult.ProblemDetails.Extensions["errorCode"].Should().Be("AI_HARD_CAP_EXCEEDED");
        problemResult.ProblemDetails.Extensions["restaurantId"].Should().Be(restaurantId);
        problemResult.ProblemDetails.Extensions["billingCycle"].Should().Be("2026-11");
        problemResult.ProblemDetails.Extensions["overageAllowed"].Should().Be(false);
    }

    [Fact]
    public async Task StreamChatEndpoint_WhenWriteIntent_StreamsSseEventsIncludingActionProposal()
    {
        IClock clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Now);

        RedisQuotaEngine quotaEngine = new(clock, redis: null, defaultBaseQuota: 250_000);
        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        DefaultHttpContext httpContext = CreateAuthenticatedHttpContext(restaurantId, userId, ["KitchenStaff"]);
        using MemoryStream responseStream = new();
        httpContext.Response.Body = responseStream;

        StubRestaurantProvider restaurantProvider = new(restaurantId);
        ActionProposalSigner signer = new("Orderly-Phase5-Secret-Key-32Bytes!!", clock);
        IHttpClientFactory httpFactory = Substitute.For<IHttpClientFactory>();

        IAiToolPlugin[] tools =
        [
            new KitchenTicketsReadPlugin(httpFactory),
            new Catalog86ReadPlugin(httpFactory),
            new ToggleItem86ProposalPlugin(signer),
            new BumpTicketProposalPlugin(signer)
        ];

        RestaurantContextSnapshotService snapshotService = new(clock);
        DbContextOptions<AiDbContext> dbOptions = new DbContextOptionsBuilder<AiDbContext>()
            .UseInMemoryDatabase($"ai-sse-{Guid.NewGuid()}")
            .Options;
        using AiDbContext dbContext = new(dbOptions, restaurantProvider);
        AiOutboxPublisher outboxPublisher = new(dbContext);

        TieredAiOrchestrator orchestrator = new(
            tools,
            new RoleToolFilter(),
            snapshotService,
            quotaEngine,
            outboxPublisher,
            dbContext,
            clock);

        StreamChatRequestDto request = new(
            RestaurantId: restaurantId,
            ConversationId: "conv-sse-1",
            Prompt: "Toggle 86 status on Ribeye Steak");

        await StreamChatEndpoint.HandleStreamChatAsync(
            request,
            httpContext,
            restaurantProvider,
            quotaEngine,
            orchestrator,
            CancellationToken.None);

        string sseOutput = Encoding.UTF8.GetString(responseStream.ToArray());
        sseOutput.Should().Contain("event: message_delta");
        sseOutput.Should().Contain("event: action_proposal");
        sseOutput.Should().Contain("toggle_item_86");
        sseOutput.Should().Contain("event: turn_completed");
    }

    [Fact]
    public async Task ExecuteProposalEndpoint_VerifiesSignatureAndExecutesDeterministicallyWithZeroLlmTokens()
    {
        IClock clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Now);

        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        ActionProposalSigner signer = new("Orderly-Phase5-Secret-Key-32Bytes!!", clock);

        ActionProposalDto validProposal = signer.CreateSignedProposal(
            restaurantId: restaurantId,
            userId: userId,
            actionName: "toggle_item_86",
            downstreamService: "Catalog",
            httpMethod: "PATCH",
            relativePath: $"/v1/restaurants/{restaurantId}/menu-items/101/availability",
            parametersJson: """{"menuItemId":"101","isAvailable":false}""",
            summary: "Toggle 86 status on Ribeye Steak",
            impactNotice: "Updates POS & KDS");

        DefaultHttpContext httpContext = CreateAuthenticatedHttpContext(restaurantId, userId, ["KitchenStaff"]);
        StubRestaurantProvider restaurantProvider = new(restaurantId);

        int downstreamCalls = 0;
        StubDownstreamHandler handler = new(() => Interlocked.Increment(ref downstreamCalls));
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("Catalog").Returns(_ => new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://catalog.api:8080")
        });

        // 1. Valid proposal executes downstream HTTP mutation with 0 LLM tokens consumed
        IResult okResult = await ExecuteProposalEndpoint.HandleExecuteProposalAsync(
            new ExecuteProposalRequestDto(validProposal),
            httpContext,
            restaurantProvider,
            signer,
            httpClientFactory,
            clock,
            CancellationToken.None);

        Ok<ExecuteProposalResponseDto> okTyped = okResult.Should().BeOfType<Ok<ExecuteProposalResponseDto>>().Subject;
        okTyped.Value!.Status.Should().Be("Executed");
        okTyped.Value.LlmTokensConsumed.Should().Be(0);
        downstreamCalls.Should().Be(1);

        // 2. Tampered proposal fails with 403 Forbidden and never calls downstream
        ActionProposalDto tampered = validProposal with { ParametersJson = """{"menuItemId":"999","isAvailable":true}""" };
        IResult forbiddenResult = await ExecuteProposalEndpoint.HandleExecuteProposalAsync(
            new ExecuteProposalRequestDto(tampered),
            httpContext,
            restaurantProvider,
            signer,
            httpClientFactory,
            clock,
            CancellationToken.None);

        ProblemHttpResult problemTyped = forbiddenResult.Should().BeOfType<ProblemHttpResult>().Subject;
        problemTyped.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problemTyped.ProblemDetails.Extensions["errorCode"].Should().Be("AI_PROPOSAL_SIGNATURE_INVALID");
        downstreamCalls.Should().Be(1);
    }

    [Fact]
    public async Task QuotaEndpoints_EnforceAdminRoleAndSyncSafetyCeilingToRedisEngine()
    {
        IClock clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Now);

        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        StubRestaurantProvider restaurantProvider = new(restaurantId);
        RedisQuotaEngine quotaEngine = new(clock, redis: null, defaultBaseQuota: 250_000);

        DbContextOptions<AiDbContext> dbOptions = new DbContextOptionsBuilder<AiDbContext>()
            .UseInMemoryDatabase($"ai-quota-ep-{Guid.NewGuid()}")
            .Options;
        using AiDbContext dbContext = new(dbOptions, restaurantProvider);

        UpdateQuotaConfigRequestDto updateRequest = new(
            AllowOverage: true,
            MaxOverageTokens: 500_000,
            MaxOverageSpendUsd: 50.00m,
            AutoPurchasePacksEnabled: true);

        // 1. KitchenStaff attempt -> 403 Forbid
        DefaultHttpContext staffHttpContext = CreateAuthenticatedHttpContext(restaurantId, userId, ["KitchenStaff"]);
        IResult staffAttempt = await UpdateQuotaConfigEndpoint.HandleUpdateQuotaConfigAsync(
            restaurantId,
            updateRequest,
            staffHttpContext,
            restaurantProvider,
            dbContext,
            quotaEngine,
            clock,
            CancellationToken.None);
        staffAttempt.Should().BeOfType<ForbidHttpResult>();

        // 2. RestaurantAdmin attempt -> 200 OK & synced
        DefaultHttpContext adminHttpContext = CreateAuthenticatedHttpContext(restaurantId, userId, ["RestaurantAdmin"]);
        IResult adminAttempt = await UpdateQuotaConfigEndpoint.HandleUpdateQuotaConfigAsync(
            restaurantId,
            updateRequest,
            adminHttpContext,
            restaurantProvider,
            dbContext,
            quotaEngine,
            clock,
            CancellationToken.None);

        Ok<TenantQuotaStatusDto> okStatus = adminAttempt.Should().BeOfType<Ok<TenantQuotaStatusDto>>().Subject;
        okStatus.Value!.AllowOverage.Should().BeTrue();
        okStatus.Value.MaxOverageTokens.Should().Be(500_000);
        okStatus.Value.MaxOverageSpendUsd.Should().Be(50.00m);

        // 3. Enqueue background job endpoint returns 202 Accepted
        ServiceCollection services = new();
        using ServiceProvider sp = services.BuildServiceProvider();
        IHubContext<AssistantHub, IAssistantHubClient> hubContext = Substitute.For<IHubContext<AssistantHub, IAssistantHubClient>>();
        using AiBackgroundWorker worker = new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            hubContext,
            clock,
            NullLogger<AiBackgroundWorker>.Instance);
        AiOutboxPublisher outbox = new(dbContext);

        IResult jobAccepted = await EnqueueAiJobEndpoint.HandleEnqueueJobAsync(
            new EnqueueAiJobRequestDto(restaurantId, "conv-job-1", "Audit evening shift margins"),
            adminHttpContext,
            restaurantProvider,
            quotaEngine,
            worker,
            outbox,
            dbContext,
            clock,
            CancellationToken.None);

        Accepted<EnqueueAiJobResponseDto> acceptedTyped = jobAccepted.Should().BeOfType<Accepted<EnqueueAiJobResponseDto>>().Subject;
        acceptedTyped.Value!.Status.Should().Be("Accepted");
        worker.GetJobState(acceptedTyped.Value.JobId).Should().NotBeNull();
    }

    private static DefaultHttpContext CreateAuthenticatedHttpContext(
        Guid restaurantId,
        Guid userId,
        IReadOnlyList<string> roles)
    {
        List<Claim> claims =
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("restaurantId", restaurantId.ToString())
        ];

        foreach (string role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        DefaultHttpContext context = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestBearer"))
        };
        return context;
    }

    private sealed class StubRestaurantProvider(Guid restaurantId) : ICurrentRestaurantProvider
    {
        public Guid RestaurantId { get; } = restaurantId;

        public IDisposable Attach(ClaimsPrincipal principal) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class StubDownstreamHandler(Action onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequest();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"updated":true}""")
            });
        }
    }
}
