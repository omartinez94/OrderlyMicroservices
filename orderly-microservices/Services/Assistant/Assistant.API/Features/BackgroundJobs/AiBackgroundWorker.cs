using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.Channels;
using Assistant.API.Features.Chat;

namespace Assistant.API.Features.BackgroundJobs;

public sealed record AiBackgroundJob(
    Guid JobId,
    Guid RestaurantId,
    Guid UserId,
    string ConversationId,
    string Prompt,
    string JobType,
    Guid ReservationId,
    IReadOnlyList<string> CallerRoles,
    Instant RequestedAt);

public sealed record AiBackgroundJobState(
    Guid JobId,
    Guid RestaurantId,
    Guid UserId,
    string ConversationId,
    string Status,
    int ProgressPercent,
    string? ResultMarkdown,
    int EffectiveTokensUsed,
    Instant UpdatedAt);

public interface IAiBackgroundJobQueue
{
    ValueTask EnqueueAsync(AiBackgroundJob job, CancellationToken cancellationToken = default);
    AiBackgroundJobState? GetJobState(Guid jobId);
    Task<AiBackgroundJobState> ProcessJobNowAsync(AiBackgroundJob job, CancellationToken cancellationToken = default);
}

/// <summary>
/// Bounded background worker (<see cref="Channel{T}"/> + per-tenant <see cref="SemaphoreSlim"/> max concurrency = 2)
/// that executes Tier 2 analytical AI prompts off the HTTP request thread, settles Redis quota,
/// and broadcasts <c>AiJobProgress</c> and <c>AiJobCompleted</c> via SignalR (<c>/hubs/assistant</c>).
/// </summary>
public sealed class AiBackgroundWorker : BackgroundService, IAiBackgroundJobQueue
{
    public const int MaxConcurrentJobsPerTenant = 2;

    private readonly Channel<AiBackgroundJob> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<AssistantHub, IAssistantHubClient> _hubContext;
    private readonly IClock _clock;
    private readonly ILogger<AiBackgroundWorker> _logger;

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _tenantSemaphores = new();
    private readonly ConcurrentDictionary<Guid, AiBackgroundJobState> _jobStates = new();

    public AiBackgroundWorker(
        IServiceScopeFactory scopeFactory,
        IHubContext<AssistantHub, IAssistantHubClient> hubContext,
        IClock clock,
        ILogger<AiBackgroundWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _clock = clock;
        _logger = logger;

        BoundedChannelOptions options = new(capacity: 256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<AiBackgroundJob>(options);
    }

    public async ValueTask EnqueueAsync(AiBackgroundJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        Instant now = _clock.GetCurrentInstant();
        _jobStates[job.JobId] = new AiBackgroundJobState(
            JobId: job.JobId,
            RestaurantId: job.RestaurantId,
            UserId: job.UserId,
            ConversationId: job.ConversationId,
            Status: "Queued",
            ProgressPercent: 0,
            ResultMarkdown: null,
            EffectiveTokensUsed: 0,
            UpdatedAt: now);

        await _channel.Writer.WriteAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public AiBackgroundJobState? GetJobState(Guid jobId) =>
        _jobStates.TryGetValue(jobId, out AiBackgroundJobState? state) ? state : null;

    public async Task<AiBackgroundJobState> ProcessJobNowAsync(
        AiBackgroundJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        SemaphoreSlim tenantGate = _tenantSemaphores.GetOrAdd(
            job.RestaurantId,
            _ => new SemaphoreSlim(MaxConcurrentJobsPerTenant, MaxConcurrentJobsPerTenant));

        await tenantGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Instant startedAt = _clock.GetCurrentInstant();
            AiJobProgressMessage progressMsg = new(
                JobId: job.JobId,
                RestaurantId: job.RestaurantId,
                ConversationId: job.ConversationId,
                Status: "Running",
                ProgressPercent: 35,
                StepDescription: "Aggregating live snapshot & querying Tier-2 analytical model...",
                Timestamp: startedAt);

            _jobStates[job.JobId] = new AiBackgroundJobState(
                job.JobId,
                job.RestaurantId,
                job.UserId,
                job.ConversationId,
                "Running",
                35,
                null,
                0,
                startedAt);

            await BroadcastProgressAsync(job, progressMsg).ConfigureAwait(false);

            using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ICurrentRestaurantProvider restaurantProvider = scope.ServiceProvider.GetRequiredService<ICurrentRestaurantProvider>();
            ClaimsPrincipal syntheticPrincipal = new(new ClaimsIdentity(
            [
                new Claim("restaurantId", job.RestaurantId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, job.UserId.ToString())
            ]));

            using IDisposable tenantScope = restaurantProvider.Attach(syntheticPrincipal);
            IAiAssistantOrchestrator orchestrator = scope.ServiceProvider.GetRequiredService<IAiAssistantOrchestrator>();

            AiChatTurnRequest turnRequest = new(
                RestaurantId: job.RestaurantId,
                UserId: job.UserId,
                ConversationId: job.ConversationId,
                Prompt: job.Prompt,
                CallerRoles: job.CallerRoles);

            AiOrchestrationSummary summary = await orchestrator
                .ExecuteTurnAsync(turnRequest, job.ReservationId, cancellationToken)
                .ConfigureAwait(false);

            Instant completedAt = _clock.GetCurrentInstant();
            AiBackgroundJobState completedState = new(
                JobId: job.JobId,
                RestaurantId: job.RestaurantId,
                UserId: job.UserId,
                ConversationId: job.ConversationId,
                Status: "Completed",
                ProgressPercent: 100,
                ResultMarkdown: summary.ResponseText,
                EffectiveTokensUsed: summary.EffectiveBillableTokens,
                UpdatedAt: completedAt);

            _jobStates[job.JobId] = completedState;

            AiJobCompletedMessage completedMsg = new(
                JobId: job.JobId,
                RestaurantId: job.RestaurantId,
                ConversationId: job.ConversationId,
                Status: "Completed",
                SelectedModel: TieredAiOrchestrator.Tier2DeepModel,
                ResultMarkdown: summary.ResponseText,
                EffectiveTokensUsed: summary.EffectiveBillableTokens,
                CompletedAt: completedAt);

            await BroadcastCompletedAsync(job, completedMsg).ConfigureAwait(false);
            return completedState;
        }
        finally
        {
            tenantGate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (AiBackgroundJob job in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessJobNowAsync(job, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Background AI job {JobId} failed for restaurant {RestaurantId}", job.JobId, job.RestaurantId);
                }
            }, stoppingToken);
        }
    }

    private async Task BroadcastProgressAsync(AiBackgroundJob job, AiJobProgressMessage message)
    {
        await _hubContext.Clients.Group($"restaurant:{job.RestaurantId}").AiJobProgress(message).ConfigureAwait(false);
        if (job.UserId != Guid.Empty)
        {
            await _hubContext.Clients.Group($"user:{job.UserId}").AiJobProgress(message).ConfigureAwait(false);
        }
    }

    private async Task BroadcastCompletedAsync(AiBackgroundJob job, AiJobCompletedMessage message)
    {
        await _hubContext.Clients.Group($"restaurant:{job.RestaurantId}").AiJobCompleted(message).ConfigureAwait(false);
        if (job.UserId != Guid.Empty)
        {
            await _hubContext.Clients.Group($"user:{job.UserId}").AiJobCompleted(message).ConfigureAwait(false);
        }
    }
}
