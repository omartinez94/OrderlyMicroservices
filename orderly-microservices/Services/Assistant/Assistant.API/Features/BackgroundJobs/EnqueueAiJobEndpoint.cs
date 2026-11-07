using Assistant.API.Features.Chat;
using Assistant.API.Infrastructure.Metering;
using Assistant.API.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

namespace Assistant.API.Features.BackgroundJobs;

public sealed record EnqueueAiJobRequestDto(
    Guid? RestaurantId,
    string? ConversationId,
    string Prompt,
    string? JobType = null,
    int? EstimatedTokens = null);

public sealed record EnqueueAiJobResponseDto(
    Guid JobId,
    Guid RestaurantId,
    string ConversationId,
    string Status,
    Instant RequestedAt);

public sealed class EnqueueAiJobEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/jobs", HandleEnqueueJobAsync)
            .RequireAuthorization()
            .WithName("EnqueueAiBackgroundJob")
            .WithTags("Assistant")
            .WithSummary("Reserves quota and enqueues a detached Tier-2 analytical AI background job (202 Accepted).");

        app.MapGet("/v1/jobs/{jobId:guid}", HandleGetJobStatus)
            .RequireAuthorization()
            .WithName("GetAiBackgroundJobStatus")
            .WithTags("Assistant")
            .WithSummary("Returns current execution status and result markdown for a background AI job.");
    }

    public static async Task<IResult> HandleEnqueueJobAsync(
        EnqueueAiJobRequestDto request,
        HttpContext httpContext,
        ICurrentRestaurantProvider restaurantProvider,
        IAiQuotaEngine quotaEngine,
        IAiBackgroundJobQueue jobQueue,
        IOutboxPublisher outboxPublisher,
        AiDbContext dbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid restaurantId = StreamChatEndpoint.ResolveRestaurantId(
            request.RestaurantId,
            httpContext.User,
            restaurantProvider);

        if (restaurantId == Guid.Empty)
        {
            return Results.Forbid();
        }

        Guid userId = StreamChatEndpoint.ResolveUserId(httpContext.User);
        int estimatedTokens = request.EstimatedTokens is > 0 ? request.EstimatedTokens.Value : 3_500;

        QuotaReservationResult reservation = await quotaEngine.ReserveQuotaAsync(
            restaurantId,
            estimatedTokens,
            Duration.FromMinutes(5),
            cancellationToken).ConfigureAwait(false);

        if (!reservation.Allowed)
        {
            return StreamChatEndpoint.BuildQuotaExceededProblemResult(restaurantId, reservation);
        }

        Guid jobId = Guid.NewGuid();
        Instant now = clock.GetCurrentInstant();
        string conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString("N")
            : request.ConversationId;
        string jobType = string.IsNullOrWhiteSpace(request.JobType) ? "DeepAnalyticsJob" : request.JobType;
        IReadOnlyList<string> callerRoles = httpContext.User.GetRoles().ToList();

        AiBackgroundJob job = new(
            JobId: jobId,
            RestaurantId: restaurantId,
            UserId: userId,
            ConversationId: conversationId,
            Prompt: request.Prompt ?? string.Empty,
            JobType: jobType,
            ReservationId: reservation.ReservationId,
            CallerRoles: callerRoles,
            RequestedAt: now);

        AiBackgroundJobRequestedEvent integrationEvent = new()
        {
            JobId = jobId,
            RestaurantId = restaurantId,
            UserId = userId,
            ConversationId = conversationId,
            Prompt = job.Prompt,
            JobType = jobType,
            ReservationId = reservation.ReservationId,
            EstimatedTokens = estimatedTokens,
            CallerRoles = callerRoles,
            RequestedAt = now
        };

        await outboxPublisher.PublishAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await jobQueue.EnqueueAsync(job, cancellationToken).ConfigureAwait(false);

        EnqueueAiJobResponseDto response = new(
            JobId: jobId,
            RestaurantId: restaurantId,
            ConversationId: conversationId,
            Status: "Accepted",
            RequestedAt: now);

        return Results.Accepted($"/v1/jobs/{jobId}", response);
    }

    public static IResult HandleGetJobStatus(
        Guid jobId,
        IAiBackgroundJobQueue jobQueue)
    {
        AiBackgroundJobState? state = jobQueue.GetJobState(jobId);
        return state is null
            ? Results.NotFound()
            : Results.Ok(state);
    }
}
