using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Assistant.API.Domain.Entities;
using StackExchange.Redis;

namespace Assistant.API.Infrastructure.Metering;

/// <summary>
/// Self-healing two-phase token quota metering engine.
/// Executes atomic Lua scripts (<c>ai_reserve_quota.lua</c> and <c>ai_settle_quota.lua</c>) against Redis
/// when <see cref="IConnectionMultiplexer"/> is connected, with an identical lock-synchronized atomic ZSET
/// state machine fallback for local/in-memory test harnesses.
/// </summary>
public sealed class RedisQuotaEngine : IAiQuotaEngine
{
    public const string ReserveLuaResourceName = "Assistant.API.Infrastructure.Metering.LuaScripts.ai_reserve_quota.lua";
    public const string SettleLuaResourceName = "Assistant.API.Infrastructure.Metering.LuaScripts.ai_settle_quota.lua";

    private static readonly Duration DefaultReservationTtl = Duration.FromMinutes(2);
    private static readonly string ReserveLuaScript = LoadEmbeddedScript(ReserveLuaResourceName);
    private static readonly string SettleLuaScript = LoadEmbeddedScript(SettleLuaResourceName);

    private readonly IClock _clock;
    private readonly IConnectionMultiplexer? _redis;
    private readonly long _defaultBaseQuota;

    // Atomic per-tenant in-memory state used when Redis is not connected (unit/concurrency tests & local fallback)
    private readonly ConcurrentDictionary<string, object> _tenantLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, QuotaCycleState> _cycleStates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, TenantConfigCacheEntry> _configCache = new();

    public RedisQuotaEngine(
        IClock clock,
        IConnectionMultiplexer? redis = null,
        long defaultBaseQuota = RestaurantAiQuotaConfig.DefaultIncludedTokensPerCycle)
    {
        _clock = clock;
        _redis = redis;
        _defaultBaseQuota = defaultBaseQuota > 0
            ? defaultBaseQuota
            : RestaurantAiQuotaConfig.DefaultIncludedTokensPerCycle;
    }

    public static string GetReserveLuaScript() => ReserveLuaScript;

    public static string GetSettleLuaScript() => SettleLuaScript;

    public static string GetBillingCycleKey(Instant instant, out string displayBillingCycle)
    {
        ZonedDateTime utc = instant.InUtc();
        displayBillingCycle = string.Create(
            CultureInfo.InvariantCulture,
            $"{utc.Year:D4}-{utc.Month:D2}");
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{utc.Year:D4}{utc.Month:D2}");
    }

    public async Task<QuotaReservationResult> ReserveQuotaAsync(
        Guid restaurantId,
        int estimatedTokens,
        Duration? reservationTtl = null,
        CancellationToken cancellationToken = default)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new ArgumentException("RestaurantId must be non-empty.", nameof(restaurantId));
        }

        if (estimatedTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedTokens), "EstimatedTokens must be positive.");
        }

        Instant now = _clock.GetCurrentInstant();
        long nowMs = now.ToUnixTimeMilliseconds();
        long ttlMs = (long)(reservationTtl ?? DefaultReservationTtl).TotalMilliseconds;
        Guid reservationId = Guid.NewGuid();
        string cycleCompact = GetBillingCycleKey(now, out string displayCycle);

        if (_redis is { IsConnected: true })
        {
            IDatabase db = _redis.GetDatabase();
            RedisKey[] keys =
            [
                $"ai:quota:{restaurantId}:{cycleCompact}",
                $"ai:res_zset:{restaurantId}:{cycleCompact}",
                $"ai:res_amt:{restaurantId}:{cycleCompact}",
                $"ai:config:{restaurantId}"
            ];
            RedisValue[] values =
            [
                reservationId.ToString("N"),
                estimatedTokens,
                nowMs,
                ttlMs,
                _defaultBaseQuota
            ];

            RedisResult raw = await db.ScriptEvaluateAsync(ReserveLuaScript, keys, values).ConfigureAwait(false);
            RedisResult[] items = (RedisResult[])raw!;
            int statusCode = (int)items[0];
            string reason = (string)items[1]!;
            long committed = (long)items[2];
            long limit = (long)items[3];

            return new QuotaReservationResult(
                Allowed: statusCode == 1,
                StatusCode: statusCode,
                StatusReason: reason,
                ReservationId: statusCode == 1 ? reservationId : Guid.Empty,
                CommittedTokens: committed,
                LimitTokens: limit,
                BillingCycle: displayCycle);
        }

        return ReserveInMemoryAtomic(
            restaurantId,
            cycleCompact,
            displayCycle,
            reservationId,
            estimatedTokens,
            nowMs,
            ttlMs);
    }

    public async Task<QuotaSettlementResult> SettleQuotaAsync(
        Guid restaurantId,
        Guid reservationId,
        int uncachedPromptTokens,
        int cachedPromptTokens,
        int completionTokens,
        string model,
        CancellationToken cancellationToken = default)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new ArgumentException("RestaurantId must be non-empty.", nameof(restaurantId));
        }

        Instant now = _clock.GetCurrentInstant();
        string cycleCompact = GetBillingCycleKey(now, out string displayCycle);
        int safeUncached = Math.Max(0, uncachedPromptTokens);
        int safeCached = Math.Max(0, cachedPromptTokens);
        int safeCompletion = Math.Max(0, completionTokens);

        long newEffective;
        int effectiveDelta;

        if (_redis is { IsConnected: true })
        {
            IDatabase db = _redis.GetDatabase();
            RedisKey[] keys =
            [
                $"ai:quota:{restaurantId}:{cycleCompact}",
                $"ai:res_zset:{restaurantId}:{cycleCompact}",
                $"ai:res_amt:{restaurantId}:{cycleCompact}"
            ];
            RedisValue[] values =
            [
                reservationId.ToString("N"),
                safeUncached,
                safeCached,
                safeCompletion
            ];

            RedisResult raw = await db.ScriptEvaluateAsync(SettleLuaScript, keys, values).ConfigureAwait(false);
            RedisResult[] items = (RedisResult[])raw!;
            newEffective = (long)items[0];
            effectiveDelta = (int)items[1];
        }
        else
        {
            (newEffective, effectiveDelta) = SettleInMemoryAtomic(
                restaurantId,
                cycleCompact,
                reservationId,
                safeUncached,
                safeCached,
                safeCompletion);
        }

        TenantConfigCacheEntry config = GetOrDefaultConfig(restaurantId);
        bool wasOverage = newEffective > config.BaseQuota;
        decimal estimatedCostUsd = EstimateCostUsd(effectiveDelta, model);

        return new QuotaSettlementResult(
            TotalEffectiveUsedTokens: newEffective,
            EffectiveDeltaTokens: effectiveDelta,
            WasOverage: wasOverage,
            EstimatedCostUsd: estimatedCostUsd,
            BillingCycle: displayCycle);
    }

    public async Task ReleaseReservationAsync(
        Guid restaurantId,
        Guid reservationId,
        CancellationToken cancellationToken = default)
    {
        if (restaurantId == Guid.Empty || reservationId == Guid.Empty)
        {
            return;
        }

        Instant now = _clock.GetCurrentInstant();
        string cycleCompact = GetBillingCycleKey(now, out _);

        if (_redis is { IsConnected: true })
        {
            IDatabase db = _redis.GetDatabase();
            string resIdStr = reservationId.ToString("N");
            await db.SortedSetRemoveAsync($"ai:res_zset:{restaurantId}:{cycleCompact}", resIdStr).ConfigureAwait(false);
            await db.HashDeleteAsync($"ai:res_amt:{restaurantId}:{cycleCompact}", resIdStr).ConfigureAwait(false);
            return;
        }

        string stateKey = $"{restaurantId}:{cycleCompact}";
        object gate = _tenantLocks.GetOrAdd(stateKey, _ => new object());
        lock (gate)
        {
            if (_cycleStates.TryGetValue(stateKey, out QuotaCycleState? state))
            {
                string key = reservationId.ToString("N");
                state.ReservationExpirationsMs.Remove(key);
                state.ReservationAmounts.Remove(key);
            }
        }
    }

    public async Task<TenantQuotaStatusDto> GetQuotaStatusAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        Instant now = _clock.GetCurrentInstant();
        long nowMs = now.ToUnixTimeMilliseconds();
        string cycleCompact = GetBillingCycleKey(now, out string displayCycle);
        TenantConfigCacheEntry config = GetOrDefaultConfig(restaurantId);

        long usedEffective = 0;
        long activeReserved = 0;

        if (_redis is { IsConnected: true })
        {
            IDatabase db = _redis.GetDatabase();
            string quotaKey = $"ai:quota:{restaurantId}:{cycleCompact}";
            string resZsetKey = $"ai:res_zset:{restaurantId}:{cycleCompact}";
            string resAmtKey = $"ai:res_amt:{restaurantId}:{cycleCompact}";

            RedisValue[] expired = await db.SortedSetRangeByScoreAsync(resZsetKey, double.NegativeInfinity, nowMs).ConfigureAwait(false);
            if (expired.Length > 0)
            {
                await db.SortedSetRemoveRangeByScoreAsync(resZsetKey, double.NegativeInfinity, nowMs).ConfigureAwait(false);
                await db.HashDeleteAsync(resAmtKey, expired).ConfigureAwait(false);
            }

            RedisValue usedVal = await db.HashGetAsync(quotaKey, "used_effective").ConfigureAwait(false);
            if (usedVal.HasValue && long.TryParse(usedVal.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedUsed))
            {
                usedEffective = parsedUsed;
            }

            RedisValue[] activeVals = await db.HashValuesAsync(resAmtKey).ConfigureAwait(false);
            foreach (RedisValue val in activeVals)
            {
                if (long.TryParse(val.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long amt))
                {
                    activeReserved += amt;
                }
            }
        }
        else
        {
            string stateKey = $"{restaurantId}:{cycleCompact}";
            object gate = _tenantLocks.GetOrAdd(stateKey, _ => new object());
            lock (gate)
            {
                QuotaCycleState state = _cycleStates.GetOrAdd(stateKey, _ => new QuotaCycleState());
                EvictExpiredHoldsUnderLock(state, nowMs);
                usedEffective = state.UsedEffective;
                activeReserved = state.ReservationAmounts.Values.Sum();
            }
        }

        long totalCommitted = usedEffective + activeReserved;
        string statusTier = ResolveStatusTier(totalCommitted, config);
        long overageTokensUsed = Math.Max(0L, totalCommitted - config.BaseQuota);
        decimal estimatedOverageSpendUsd = Math.Round((overageTokensUsed / 100_000m) * config.PricePerPackUsd, 4);

        ZonedDateTime utcNow = now.InUtc();
        LocalDate nextMonthStart = new LocalDate(utcNow.Year, utcNow.Month, 1).PlusMonths(1);
        Instant resetsAt = nextMonthStart.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
        int daysUntilReset = Math.Max(1, (int)Math.Ceiling((resetsAt - now).TotalDays));

        return new TenantQuotaStatusDto(
            RestaurantId: restaurantId,
            BillingCycle: displayCycle,
            StatusTier: statusTier,
            UsedEffectiveTokens: usedEffective,
            ActiveReservedTokens: activeReserved,
            IncludedTokensLimit: config.BaseQuota,
            AllowOverage: config.AllowOverage,
            MaxOverageTokens: config.OverageLimit,
            MaxOverageSpendUsd: config.MaxOverageSpendUsd,
            EstimatedOverageSpendUsd: estimatedOverageSpendUsd,
            IsSuspended: config.IsSuspended,
            DaysUntilReset: daysUntilReset,
            ResetsAt: resetsAt);
    }

    public async Task SyncTenantConfigAsync(
        RestaurantAiQuotaConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        TenantConfigCacheEntry entry = new(
            BaseQuota: config.IncludedTokensPerCycle,
            AllowOverage: config.AllowOverage,
            OverageLimit: config.MaxOverageTokens,
            MaxOverageSpendUsd: config.MaxOverageSpendUsd,
            PricePerPackUsd: config.PricePerPackUsd > 0m ? config.PricePerPackUsd : RestaurantAiQuotaConfig.DefaultPricePerPackUsd,
            IsSuspended: config.IsSuspended);

        _configCache[config.RestaurantId] = entry;

        if (_redis is { IsConnected: true })
        {
            IDatabase db = _redis.GetDatabase();
            string configKey = $"ai:config:{config.RestaurantId}";
            HashEntry[] entries =
            [
                new HashEntry("base_quota", entry.BaseQuota),
                new HashEntry("allow_overage", entry.AllowOverage ? 1 : 0),
                new HashEntry("overage_limit", entry.OverageLimit),
                new HashEntry("is_suspended", entry.IsSuspended ? 1 : 0)
            ];
            await db.HashSetAsync(configKey, entries).ConfigureAwait(false);
            await db.KeyExpireAsync(configKey, TimeSpan.FromHours(1)).ConfigureAwait(false);
        }
    }

    private static string ResolveStatusTier(long committedTokens, TenantConfigCacheEntry config)
    {
        if (config.IsSuspended)
        {
            return "capped";
        }

        if (committedTokens >= config.BaseQuota)
        {
            long maxAllowed = config.AllowOverage ? config.BaseQuota + config.OverageLimit : config.BaseQuota;
            return config.AllowOverage && committedTokens < maxAllowed ? "overage" : "capped";
        }

        double ratio = config.BaseQuota > 0 ? (double)committedTokens / config.BaseQuota : 1.0d;
        return ratio >= 0.80d ? "warning" : "normal";
    }

    private static decimal EstimateCostUsd(int effectiveTokens, string model)
    {
        // Fast tier ~$0.15 / 1M tokens; Deep Pro tier ~$2.50 / 1M tokens
        bool isPro = !string.IsNullOrWhiteSpace(model) &&
                     (model.Contains("pro", StringComparison.OrdinalIgnoreCase) ||
                      model.Contains("gpt-4o", StringComparison.OrdinalIgnoreCase) && !model.Contains("mini", StringComparison.OrdinalIgnoreCase));

        decimal ratePerMillion = isPro ? 2.50m : 0.20m;
        return Math.Round((effectiveTokens / 1_000_000m) * ratePerMillion, 6);
    }

    private static void EvictExpiredHoldsUnderLock(QuotaCycleState state, long nowMs)
    {
        List<string>? expiredKeys = null;
        foreach (KeyValuePair<string, long> kvp in state.ReservationExpirationsMs)
        {
            if (kvp.Value <= nowMs)
            {
                expiredKeys ??= [];
                expiredKeys.Add(kvp.Key);
            }
        }

        if (expiredKeys is null)
        {
            return;
        }

        foreach (string expiredId in expiredKeys)
        {
            state.ReservationExpirationsMs.Remove(expiredId);
            state.ReservationAmounts.Remove(expiredId);
        }
    }

    private static string LoadEmbeddedScript(string resourceName)
    {
        Assembly assembly = typeof(RedisQuotaEngine).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded Lua script '{resourceName}' was not found in assembly.");
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private QuotaReservationResult ReserveInMemoryAtomic(
        Guid restaurantId,
        string cycleCompact,
        string displayCycle,
        Guid reservationId,
        int estimatedTokens,
        long nowMs,
        long ttlMs)
    {
        string stateKey = $"{restaurantId}:{cycleCompact}";
        object gate = _tenantLocks.GetOrAdd(stateKey, _ => new object());

        lock (gate)
        {
            QuotaCycleState state = _cycleStates.GetOrAdd(stateKey, _ => new QuotaCycleState());

            // 1. Evict expired in-flight reservations (ZREMRANGEBYSCORE equivalent)
            EvictExpiredHoldsUnderLock(state, nowMs);

            // 2. Read tenant configuration
            TenantConfigCacheEntry config = GetOrDefaultConfig(restaurantId);
            if (config.IsSuspended)
            {
                return new QuotaReservationResult(
                    Allowed: false,
                    StatusCode: -1,
                    StatusReason: "TENANT_AI_SUSPENDED",
                    ReservationId: Guid.Empty,
                    CommittedTokens: 0,
                    LimitTokens: 0,
                    BillingCycle: displayCycle);
            }

            long maxAllowed = config.AllowOverage
                ? config.BaseQuota + config.OverageLimit
                : config.BaseQuota;

            // 3. Sum active non-expired reservations
            long totalReserved = 0;
            foreach (long amount in state.ReservationAmounts.Values)
            {
                totalReserved += amount;
            }

            long currentCommitted = state.UsedEffective + totalReserved;
            if (currentCommitted + estimatedTokens > maxAllowed)
            {
                if (!config.AllowOverage && (currentCommitted + estimatedTokens) > config.BaseQuota)
                {
                    return new QuotaReservationResult(
                        Allowed: false,
                        StatusCode: 0,
                        StatusReason: "HARD_CAP_EXCEEDED",
                        ReservationId: Guid.Empty,
                        CommittedTokens: currentCommitted,
                        LimitTokens: config.BaseQuota,
                        BillingCycle: displayCycle);
                }

                return new QuotaReservationResult(
                    Allowed: false,
                    StatusCode: 0,
                    StatusReason: "SAFETY_CEILING_EXCEEDED",
                    ReservationId: Guid.Empty,
                    CommittedTokens: currentCommitted,
                    LimitTokens: maxAllowed,
                    BillingCycle: displayCycle);
            }

            // 4. Record reservation hold with expiration timestamp
            string resKey = reservationId.ToString("N");
            state.ReservationExpirationsMs[resKey] = nowMs + ttlMs;
            state.ReservationAmounts[resKey] = estimatedTokens;

            return new QuotaReservationResult(
                Allowed: true,
                StatusCode: 1,
                StatusReason: "OK",
                ReservationId: reservationId,
                CommittedTokens: currentCommitted + estimatedTokens,
                LimitTokens: maxAllowed,
                BillingCycle: displayCycle);
        }
    }

    private (long NewEffective, int EffectiveDelta) SettleInMemoryAtomic(
        Guid restaurantId,
        string cycleCompact,
        Guid reservationId,
        int uncachedPromptTokens,
        int cachedPromptTokens,
        int completionTokens)
    {
        string stateKey = $"{restaurantId}:{cycleCompact}";
        object gate = _tenantLocks.GetOrAdd(stateKey, _ => new object());

        lock (gate)
        {
            QuotaCycleState state = _cycleStates.GetOrAdd(stateKey, _ => new QuotaCycleState());

            // 1. Release reservation hold
            string resKey = reservationId.ToString("N");
            state.ReservationExpirationsMs.Remove(resKey);
            state.ReservationAmounts.Remove(resKey);

            // 2. Compute effective billable tokens (cached tokens weighted at 25%)
            int discountedCached = (int)Math.Ceiling(cachedPromptTokens * AiUsageLedger.CachedTokenDiscountWeight);
            int effectiveDelta = uncachedPromptTokens + discountedCached + completionTokens;

            state.UsedUncachedPrompt += uncachedPromptTokens;
            state.UsedCachedPrompt += cachedPromptTokens;
            state.UsedCompletion += completionTokens;
            state.UsedEffective += effectiveDelta;

            return (state.UsedEffective, effectiveDelta);
        }
    }

    private TenantConfigCacheEntry GetOrDefaultConfig(Guid restaurantId)
    {
        if (_configCache.TryGetValue(restaurantId, out TenantConfigCacheEntry? entry))
        {
            return entry;
        }

        return new TenantConfigCacheEntry(
            BaseQuota: _defaultBaseQuota,
            AllowOverage: false,
            OverageLimit: 0,
            MaxOverageSpendUsd: 0.00m,
            PricePerPackUsd: RestaurantAiQuotaConfig.DefaultPricePerPackUsd,
            IsSuspended: false);
    }

    private sealed record TenantConfigCacheEntry(
        long BaseQuota,
        bool AllowOverage,
        long OverageLimit,
        decimal MaxOverageSpendUsd,
        decimal PricePerPackUsd,
        bool IsSuspended);

    private sealed class QuotaCycleState
    {
        public long UsedUncachedPrompt { get; set; }
        public long UsedCachedPrompt { get; set; }
        public long UsedCompletion { get; set; }
        public long UsedEffective { get; set; }
        public Dictionary<string, long> ReservationExpirationsMs { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> ReservationAmounts { get; } = new(StringComparer.Ordinal);
    }
}
