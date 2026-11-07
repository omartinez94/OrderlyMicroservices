using Assistant.API.Domain.Entities;

namespace Assistant.API.Infrastructure.Metering;

public sealed record QuotaReservationResult(
    bool Allowed,
    int StatusCode,
    string StatusReason,
    Guid ReservationId,
    long CommittedTokens,
    long LimitTokens,
    string BillingCycle)
{
    public bool IsHardCapExceeded => string.Equals(StatusReason, "HARD_CAP_EXCEEDED", StringComparison.Ordinal);
    public bool IsSafetyCeilingExceeded => string.Equals(StatusReason, "SAFETY_CEILING_EXCEEDED", StringComparison.Ordinal);
    public bool IsTenantSuspended => string.Equals(StatusReason, "TENANT_AI_SUSPENDED", StringComparison.Ordinal);
}

public sealed record QuotaSettlementResult(
    long TotalEffectiveUsedTokens,
    int EffectiveDeltaTokens,
    bool WasOverage,
    decimal EstimatedCostUsd,
    string BillingCycle);

public sealed record TenantQuotaStatusDto(
    Guid RestaurantId,
    string BillingCycle,
    string StatusTier,
    long UsedEffectiveTokens,
    long ActiveReservedTokens,
    long IncludedTokensLimit,
    bool AllowOverage,
    long MaxOverageTokens,
    decimal MaxOverageSpendUsd,
    decimal EstimatedOverageSpendUsd,
    bool IsSuspended,
    int DaysUntilReset,
    Instant ResetsAt);

public interface IAiQuotaEngine
{
    Task<QuotaReservationResult> ReserveQuotaAsync(
        Guid restaurantId,
        int estimatedTokens,
        Duration? reservationTtl = null,
        CancellationToken cancellationToken = default);

    Task<QuotaSettlementResult> SettleQuotaAsync(
        Guid restaurantId,
        Guid reservationId,
        int uncachedPromptTokens,
        int cachedPromptTokens,
        int completionTokens,
        string model,
        CancellationToken cancellationToken = default);

    Task ReleaseReservationAsync(
        Guid restaurantId,
        Guid reservationId,
        CancellationToken cancellationToken = default);

    Task<TenantQuotaStatusDto> GetQuotaStatusAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default);

    Task SyncTenantConfigAsync(
        RestaurantAiQuotaConfig config,
        CancellationToken cancellationToken = default);
}
