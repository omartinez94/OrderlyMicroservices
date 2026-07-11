namespace BuildingBlocks.Messaging.Events.Catalog;

/// <summary>
/// Published by <c>Catalog.API</c> when a customer submits feedback with
/// <c>OverallRating ≥ 4</c>. Notification service consumes this to
/// generate a reward code and dispatch it to the customer. Stays in
/// Catalog per §7 Phase 6.1 (Notification v1 is an out-of-plan
/// prerequisite).
/// </summary>
public record FeedbackSubmittedIntegrationEvent : IntegrationEvent
{
    /// <summary>Primary key of the feedback row.</summary>
    public int FeedbackId { get; init; }

    /// <summary>Restaurant the feedback is about (tenant scope).</summary>
    public Guid RestaurantId { get; init; }

    /// <summary>Order the feedback is for.</summary>
    public Guid OrderId { get; init; }

    /// <summary>Aggregate rating (1–5). Notification only consumes when ≥ 4.</summary>
    public int OverallRating { get; init; }

    /// <summary>Free-text customer comments (truncated to 1000 chars).</summary>
    public string? Comments { get; init; }

    /// <summary>Reward type slot (filled by the Catalog handler).</summary>
    public string RewardType { get; init; } = string.Empty;

    /// <summary>Reward description slot (filled by the Catalog handler).</summary>
    public string RewardDescription { get; init; } = string.Empty;

    /// <summary>Reward value slot (filled by the Catalog handler).</summary>
    public decimal? RewardValue { get; init; }
}