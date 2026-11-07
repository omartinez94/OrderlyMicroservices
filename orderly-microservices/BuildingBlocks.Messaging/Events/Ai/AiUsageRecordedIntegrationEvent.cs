namespace BuildingBlocks.Messaging.Events.Ai;

/// <summary>
/// Published via transactional outbox whenever an AI Assistant invocation settles tokens
/// (foreground chat turn, proposal generation, or detached background analytical job).
/// Consumed idempotently by <c>AiUsageLedgerConsumer</c> to persist the immutable billing audit row.
/// </summary>
public sealed record AiUsageRecordedIntegrationEvent : IntegrationEvent
{
    public Guid UsageRecordId { get; init; } = Guid.NewGuid();
    public Guid RestaurantId { get; init; }
    public Guid UserId { get; init; }
    public string ConversationId { get; init; } = string.Empty;
    public string OperationType { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int CachedTokens { get; init; }
    public int EffectiveBillableTokens { get; init; }
    public decimal EstimatedCostUsd { get; init; }
    public string BillingCycle { get; init; } = string.Empty;
    public bool WasOverage { get; init; }
    public Instant RecordedAt { get; init; }
}
