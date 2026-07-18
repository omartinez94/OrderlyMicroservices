using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Basket.API.RateLimiting;

/// <summary>
/// Rate-limit policy for <c>POST /api/v1/cart/checkout</c>. Single
/// policy, single key shape, single <see cref="FixedWindowRateLimiterOptions"/>
/// — extracted from Program.cs so the partition function and the
/// OnRejected callback are unit-testable without spinning up the full
/// Basket host (which would need Marten + Redis + RabbitMQ + JWT auth).
/// </summary>
/// <remarks>
/// <para><b>Why keyed on <c>(userId, restaurantId)</c>.</b> Keying on the
/// user alone lets a single user burst-charge across many restaurants
/// in one minute; keying on the restaurant alone lets a restaurant-wide
/// scraper burst across many users. The pair partitions fairly:
/// one user's six checkout attempts against the same restaurant
/// return 429 on the sixth; one user's six attempts spread across six
/// restaurants all succeed.</para>
/// <para><b>Why 5/minute.</b> Plan §0.4.8 lock. A legitimate checkout
/// flow has at most one user-action per second (network round-trip +
/// payment form submit). 5/minute leaves headroom for retries from a
/// flaky client without enabling an attacker to flood the bus.</para>
/// <para><b>Why <see cref="FixedWindowRateLimiterOptions.QueueLimit"/> = 0.</b>
/// Queueing is the wrong default for a checkout endpoint — the client
/// should see 429 immediately and back off, not wait in a server-side
/// queue that may exceed the broker's own timeout.</para>
/// </remarks>
public static class CheckoutRateLimiter
{
    /// <summary>Policy name referenced by <c>.RequireRateLimiting("checkout")</c>
    /// on the <c>POST /cart/checkout</c> route.</summary>
    public const string PolicyName = "checkout";

    /// <summary>Number of requests permitted per <see cref="Window"/> per partition.</summary>
    public const int PermitLimit = 5;

    /// <summary>Fixed-window length. Aligned to wall-clock at the limiter level
    /// (PartitionedRateLimiter creates a fresh window on first use, not on
    /// the minute boundary — first request after expiry triggers a new window).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // Static reference to the options monitor, set once at startup via
    // Configure(IOptionsMonitor<>). The rate-limit policy function and
    // OnRejected callback are static (the rate-limiter API requires static
    // delegates), so we capture the options monitor once and read
    // CurrentValue on each request — that's the IOptionsMonitor hot-reload
    // semantics: the most recent value is returned on every access, with no
    // need to re-resolve from DI per request.
    private static IOptionsMonitor<BasketProblemDetailsOptions> _options = default!;

    /// <summary>
    /// Wire the options monitor. Called once at startup (after
    /// <c>builder.Build()</c>) so the static <see cref="OnRejectedAsync"/>
    /// callback can emit the operator-owned <c>type</c> URI without
    /// taking an instance dependency.
    /// </summary>
    public static void Configure(IOptionsMonitor<BasketProblemDetailsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Partition function for the rate limiter. Tenant-scoped on
    /// <c>(userId, restaurantId)</c>; an unauthenticated principal
    /// (User.GetUserId() == Guid.Empty) lands in a single shared
    /// partition, but the route's <c>RequireAuthorization("Default")</c>
    /// filters those out before the policy runs.</summary>
    public static RateLimitPartition<string> PartitionFunc(HttpContext httpContext)
    {
        var key = $"{httpContext.User.GetUserId()}:{httpContext.User.GetRestaurantId()}";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }

    /// <summary>OnRejected callback. Emits 429 with a <c>Retry-After</c>
    /// header (seconds until the next window opens) and an
    /// <c>application/problem+json</c> body. Called by
    /// <see cref="PartitionedRateLimiter{TResource}"/> when the
    /// partition is exhausted.</summary>
    public static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            // MetadataName.RetryAfter exposes TimeSpan when present; ASP.NET Core
            // sets it automatically when AutoReplenishment=true. Convert to
            // seconds (integer) per RFC 7231 §7.1.3.
            var seconds = (int)Math.Ceiling(((TimeSpan)retryAfter).TotalSeconds);
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
        }

        // WriteAsJsonAsync sets Content-Type to "application/json;
        // charset=utf-8" itself — we override AFTER the write so the
        // RFC 7807 envelope is properly advertised.
        // The `type` URI is the canonical identifier for the problem
        // type per RFC 7807 §3.1 — clients use it as the primary
        // machine-readable key (the `title` is advisory). The base
        // URL is hot-reloadable via Basket:Problems:BaseUrl (env var
        // Basket__Problems__BaseUrl); IOptionsMonitor.CurrentValue
        // returns the most recent value on every access, so a config
        // change propagates without a redeploy. ValidateOnStart()
        // guarantees the value is present at boot, so we read it
        // directly without a fallback (no silent drift if config
        // somehow disappears at runtime).
        var baseUrl = _options.CurrentValue.BaseUrl;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                type = $"{baseUrl}too-many-requests",
                title = "Too Many Requests",
                status = StatusCodes.Status429TooManyRequests,
                detail = $"POST /api/v1/cart/checkout is rate-limited to {PermitLimit} requests per {(int)Window.TotalMinutes} minute per (userId, restaurantId). Retry after the indicated interval.",
            },
            cancellationToken);

        context.HttpContext.Response.ContentType = "application/problem+json";
    }
}
