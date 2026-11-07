using System.Collections.Concurrent;

namespace Assistant.API.Infrastructure.Metering;

/// <summary>
/// Per-tenant sliding-window rate limiter enforcing Requests Per Minute (RPM) and Tokens Per Minute (TPM)
/// guardrails before hitting downstream LLM endpoints.
/// </summary>
public sealed class SlidingWindowAiRateLimiter(
    IClock clock,
    int maxRequestsPerMinute = 30,
    int maxTokensPerMinute = 60_000)
{
    private static readonly Duration WindowDuration = Duration.FromMinutes(1);
    private readonly ConcurrentDictionary<Guid, TenantWindow> _windows = new();

    public (bool Allowed, string? RejectionReason) TryAcquire(Guid restaurantId, int estimatedTokens)
    {
        Instant now = clock.GetCurrentInstant();
        Instant cutoff = now - WindowDuration;

        TenantWindow window = _windows.GetOrAdd(restaurantId, _ => new TenantWindow());
        lock (window.SyncRoot)
        {
            window.Entries.RemoveAll(e => e.Timestamp <= cutoff);

            if (window.Entries.Count >= maxRequestsPerMinute)
            {
                return (false, "AI_RPM_RATE_LIMIT_EXCEEDED");
            }

            int tokensInWindow = window.Entries.Sum(e => e.Tokens);
            if (tokensInWindow + estimatedTokens > maxTokensPerMinute)
            {
                return (false, "AI_TPM_RATE_LIMIT_EXCEEDED");
            }

            window.Entries.Add(new WindowEntry(now, estimatedTokens));
            return (true, null);
        }
    }

    private sealed record WindowEntry(Instant Timestamp, int Tokens);

    private sealed class TenantWindow
    {
        public object SyncRoot { get; } = new();
        public List<WindowEntry> Entries { get; } = [];
    }
}
