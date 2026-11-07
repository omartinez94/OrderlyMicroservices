namespace Assistant.API.Domain.Entities;

/// <summary>
/// Append-only immutable record for every LLM invocation turn (foreground chat, proposal generation,
/// or detached background analytical job).
/// </summary>
public sealed class AiUsageLedger : Entity<Guid>, ITenantEntity
{
    public const double CachedTokenDiscountWeight = 0.25d;

    public Guid RestaurantId { get; set; }
    public Guid UserId { get; set; }
    public string ConversationId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int CachedTokens { get; set; }
    public int EffectiveBillableTokens { get; set; }

    public decimal EstimatedCostUsd { get; set; }
    public string BillingCycle { get; set; } = string.Empty;
    public bool WasOverage { get; set; }

    public Instant RecordedAt { get; set; }

    /// <summary>
    /// Computes the effective billable tokens where cached prompt tokens are weighted at 0.25x (ceiling).
    /// </summary>
    public static int CalculateEffectiveBillableTokens(
        int uncachedPromptTokens,
        int cachedPromptTokens,
        int completionTokens)
    {
        if (uncachedPromptTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(uncachedPromptTokens), "Uncached prompt tokens cannot be negative.");
        }

        if (cachedPromptTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cachedPromptTokens), "Cached prompt tokens cannot be negative.");
        }

        if (completionTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completionTokens), "Completion tokens cannot be negative.");
        }

        int discountedCached = (int)Math.Ceiling(cachedPromptTokens * CachedTokenDiscountWeight);
        return uncachedPromptTokens + discountedCached + completionTokens;
    }

    public static AiUsageLedger Create(
        Guid id,
        Guid restaurantId,
        Guid userId,
        string conversationId,
        string operationType,
        string provider,
        string model,
        int uncachedPromptTokens,
        int cachedTokens,
        int completionTokens,
        decimal estimatedCostUsd,
        string billingCycle,
        bool wasOverage,
        Instant recordedAt)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new ArgumentException("RestaurantId must be non-empty.", nameof(restaurantId));
        }

        if (string.IsNullOrWhiteSpace(billingCycle))
        {
            throw new ArgumentException("BillingCycle is required.", nameof(billingCycle));
        }

        int effectiveBillable = CalculateEffectiveBillableTokens(uncachedPromptTokens, cachedTokens, completionTokens);

        return new AiUsageLedger
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id,
            RestaurantId = restaurantId,
            UserId = userId,
            ConversationId = conversationId ?? string.Empty,
            OperationType = string.IsNullOrWhiteSpace(operationType) ? "FastChat" : operationType,
            Provider = string.IsNullOrWhiteSpace(provider) ? "Gemini" : provider,
            Model = string.IsNullOrWhiteSpace(model) ? "gemini-2.5-flash-lite" : model,
            PromptTokens = uncachedPromptTokens,
            CachedTokens = cachedTokens,
            CompletionTokens = completionTokens,
            EffectiveBillableTokens = effectiveBillable,
            EstimatedCostUsd = estimatedCostUsd,
            BillingCycle = billingCycle,
            WasOverage = wasOverage,
            RecordedAt = recordedAt
        };
    }
}
