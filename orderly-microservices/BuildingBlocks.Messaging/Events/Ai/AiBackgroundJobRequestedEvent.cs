namespace BuildingBlocks.Messaging.Events.Ai;

/// <summary>
/// Published when a manager or user enqueues a detached multi-step analytical AI job
/// so that background workers can process the request asynchronously and stream progress
/// via SignalR (<c>/hubs/assistant</c>).
/// </summary>
public sealed record AiBackgroundJobRequestedEvent : IntegrationEvent
{
    public Guid JobId { get; init; } = Guid.NewGuid();
    public Guid RestaurantId { get; init; }
    public Guid UserId { get; init; }
    public string ConversationId { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string JobType { get; init; } = string.Empty;
    public Guid ReservationId { get; init; }
    public int EstimatedTokens { get; init; }
    public IReadOnlyList<string> CallerRoles { get; init; } = [];
    public Instant RequestedAt { get; init; }
}
