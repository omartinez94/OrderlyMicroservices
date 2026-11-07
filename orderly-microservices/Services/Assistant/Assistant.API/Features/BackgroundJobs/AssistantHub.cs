using Microsoft.AspNetCore.Authorization;

namespace Assistant.API.Features.BackgroundJobs;

public sealed record AiJobProgressMessage(
    Guid JobId,
    Guid RestaurantId,
    string ConversationId,
    string Status,
    int ProgressPercent,
    string StepDescription,
    Instant Timestamp);

public sealed record AiJobCompletedMessage(
    Guid JobId,
    Guid RestaurantId,
    string ConversationId,
    string Status,
    string SelectedModel,
    string ResultMarkdown,
    int EffectiveTokensUsed,
    Instant CompletedAt);

public interface IAssistantHubClient
{
    Task AiJobProgress(AiJobProgressMessage message);
    Task AiJobCompleted(AiJobCompletedMessage message);
}

/// <summary>
/// SignalR hub at <c>/hubs/assistant</c> behind <c>[Authorize]</c>.
/// Automatically binds connected staff to <c>restaurant:{id}</c> and <c>user:{userId}</c> groups
/// so detached background AI jobs stream progress and completion notifications across route transitions.
/// </summary>
[Authorize]
public sealed class AssistantHub(ILogger<AssistantHub> logger) : Hub<IAssistantHubClient>
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync().ConfigureAwait(false);

        Guid userId = Context.User?.GetUserId() ?? Guid.Empty;
        if (userId != Guid.Empty)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}").ConfigureAwait(false);
        }

        List<Guid> restaurantIds = Context.User?
            .FindAll(c => c.Type is "restaurantIds" or "restaurantId")
            .Select(c => c.Value)
            .Where(s => Guid.TryParse(s, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToList() ?? [];

        foreach (Guid id in restaurantIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"restaurant:{id}").ConfigureAwait(false);
        }

        logger.LogInformation(
            "AssistantHub connection {ConnectionId} joined {RestaurantCount} restaurant group(s).",
            Context.ConnectionId,
            restaurantIds.Count);
    }

    public Task SubscribeConversation(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return Task.CompletedTask;
        }

        return Groups.AddToGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
    }
}
