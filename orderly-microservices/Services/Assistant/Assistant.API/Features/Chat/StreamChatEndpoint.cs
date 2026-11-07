using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Assistant.API.Infrastructure.Metering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Assistant.API.Features.Chat;

public sealed record StreamChatRequestDto(
    Guid? RestaurantId,
    string? ConversationId,
    string Prompt,
    int? EstimatedTokens = null,
    Dictionary<string, string>? ToolArguments = null);

public sealed class StreamChatEndpoint : ICarterModule
{
    private static readonly JsonSerializerOptions SseJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        .ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/chat/stream", HandleStreamChatAsync)
            .RequireAuthorization()
            .WithName("StreamAiChat")
            .WithTags("Assistant")
            .WithSummary("Streams AI Assistant chat responses and stateless ActionProposal cards via Server-Sent Events (SSE).");
    }

    public static async Task<IResult> HandleStreamChatAsync(
        StreamChatRequestDto request,
        HttpContext httpContext,
        ICurrentRestaurantProvider restaurantProvider,
        IAiQuotaEngine quotaEngine,
        IAiAssistantOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(httpContext);

        Guid restaurantId = ResolveRestaurantId(request.RestaurantId, httpContext.User, restaurantProvider);
        if (restaurantId == Guid.Empty)
        {
            return Results.Problem(
                title: "Tenant Context Missing",
                detail: "A valid restaurantId claim or parameter is required.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        Guid userId = ResolveUserId(httpContext.User);
        int estimatedTokens = request.EstimatedTokens is > 0 ? request.EstimatedTokens.Value : 1_200;

        QuotaReservationResult reservation = await quotaEngine.ReserveQuotaAsync(
            restaurantId,
            estimatedTokens,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!reservation.Allowed)
        {
            return BuildQuotaExceededProblemResult(restaurantId, reservation);
        }

        IReadOnlyList<string> callerRoles = httpContext.User.GetRoles().ToList();
        string conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString("N")
            : request.ConversationId;

        AiChatTurnRequest turnRequest = new(
            RestaurantId: restaurantId,
            UserId: userId,
            ConversationId: conversationId,
            Prompt: request.Prompt ?? string.Empty,
            CallerRoles: callerRoles,
            ExplicitToolArguments: request.ToolArguments);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream; charset=utf-8";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        try
        {
            await foreach (AiStreamFrame frame in orchestrator.StreamTurnAsync(
                turnRequest,
                reservation.ReservationId,
                cancellationToken).ConfigureAwait(false))
            {
                string jsonPayload = JsonSerializer.Serialize(frame, SseJsonOptions);
                string sseBlock = $"event: {frame.EventType}\ndata: {jsonPayload}\n\n";
                byte[] bytes = Encoding.UTF8.GetBytes(sseBlock);
                await httpContext.Response.Body.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await quotaEngine.ReleaseReservationAsync(restaurantId, reservation.ReservationId, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }

        return Results.Empty;
    }

    internal static IResult BuildQuotaExceededProblemResult(
        Guid restaurantId,
        QuotaReservationResult reservation)
    {
        bool isHardCap = reservation.IsHardCapExceeded;
        string errorCode = isHardCap
            ? "AI_HARD_CAP_EXCEEDED"
            : reservation.IsSafetyCeilingExceeded
                ? "AI_SAFETY_CEILING_REACHED"
                : "AI_TENANT_SUSPENDED";

        string typeUri = isHardCap
            ? "https://orderly.kalaa.dev/errors/ai-quota-hard-cap-exceeded"
            : "https://orderly.kalaa.dev/errors/ai-quota-safety-ceiling-reached";

        string title = isHardCap
            ? "Monthly AI Quota Exceeded"
            : "Monthly AI Safety Ceiling Reached";

        string detail = isHardCap
            ? $"Restaurant tenant has consumed all {reservation.LimitTokens:N0} included AI tokens for billing cycle {reservation.BillingCycle}."
            : $"Restaurant tenant has reached the configured AI overage safety ceiling ({reservation.LimitTokens:N0} tokens) for billing cycle {reservation.BillingCycle}.";

        ProblemDetails problem = new()
        {
            Type = typeUri,
            Title = title,
            Status = StatusCodes.Status429TooManyRequests,
            Detail = detail
        };

        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["restaurantId"] = restaurantId;
        problem.Extensions["billingCycle"] = reservation.BillingCycle;
        problem.Extensions["usedTokens"] = reservation.CommittedTokens;
        problem.Extensions["limitTokens"] = reservation.LimitTokens;
        problem.Extensions["overageAllowed"] = !isHardCap;

        return Results.Problem(problem);
    }

    internal static Guid ResolveRestaurantId(
        Guid? explicitRestaurantId,
        ClaimsPrincipal user,
        ICurrentRestaurantProvider restaurantProvider)
    {
        Guid claimRestaurantId = user.GetRestaurantId();
        if (claimRestaurantId != Guid.Empty)
        {
            if (explicitRestaurantId.HasValue &&
                explicitRestaurantId.Value != Guid.Empty &&
                explicitRestaurantId.Value != claimRestaurantId)
            {
                return Guid.Empty;
            }

            return claimRestaurantId;
        }

        if (restaurantProvider.RestaurantId != Guid.Empty)
        {
            return restaurantProvider.RestaurantId;
        }

        return explicitRestaurantId ?? Guid.Empty;
    }

    internal static Guid ResolveUserId(ClaimsPrincipal user)
    {
        Guid userId = user.GetUserId();
        if (userId != Guid.Empty)
        {
            return userId;
        }

        string? sub = user.FindFirstValue("sub");
        return Guid.TryParse(sub, out Guid parsed) ? parsed : Guid.Empty;
    }
}
