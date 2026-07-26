namespace Basket.API.Caching;

/// <summary>
/// Singleton registry of per-key <see cref="SemaphoreSlim"/> gates used
/// to coalesce concurrent <c>cache-miss</c> reads on
/// <see cref="Basket.API.Data.CachedBasketRepository"/> into a single
/// inner-repository query — the canonical "cache stampede protection"
/// (sometimes called <i>single-flight</i>) pattern.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime:</b> Singleton. The <see cref="SemaphoreSlim"/> entries
/// outlive the request scope — re-creating one per request would defeat
/// the coalescing entirely (every request would just take its own lock,
/// so 100 concurrent requests still produce 100 inner queries). Lock
/// creation is a <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd"/>
/// on first acquire; the entry persists for the host's lifetime and is
/// disposed in <see cref="IAsyncDisposable.DisposeAsync"/>.
/// </para>
/// <para>
/// <b>Host shutdown:</b> <see cref="IAsyncDisposable.DisposeAsync"/>
/// cancels the registry-wide <see cref="CancellationTokenSource"/>, so
/// any caller currently <c>await</c>-ing on
/// <see cref="AcquireAsync(string, CancellationToken)"/> raises
/// <see cref="OperationCanceledException"/> promptly. The host's
/// <c>IHostOptions.ShutdownTimeout</c> (default 30s) bounds the
/// drain; callers honour the supplied <see cref="CancellationToken"/>
/// so request scoping is unaffected.
/// </para>
/// <para>
/// <b>Failure semantics:</b> dispose-time cancellation logs at
/// Information, not Error — a pending waiter being cancelled at host
/// shutdown is expected, not anomalous. Acquire-time cancellation (the
/// caller's own <see cref="CancellationToken"/> firing) propagates
/// without any registry-side state change.
/// </para>
/// </remarks>
public interface IBasketCacheLockRegistry : IAsyncDisposable
{
    /// <summary>
    /// Acquires the per-<paramref name="cacheKey"/> gate, blocking until
    /// the gate is free. The returned <see cref="IAsyncDisposable"/>
    /// releases the gate on <c>DisposeAsync</c>.
    /// </summary>
    /// <remarks>
    /// <b>Caller contract:</b> <c>await using</c> the returned handle:
    /// <code>
    /// await using (await registry.AcquireAsync(cacheKey, ct))
    /// {
    ///     // critical section — only one caller per key at a time
    /// }
    /// </code>
    /// Exiting the <c>await using</c> block (normally OR via exception)
    /// releases the gate so the next waiter proceeds.
    /// </remarks>
    ValueTask<IAsyncDisposable> AcquireAsync(string cacheKey, CancellationToken cancellationToken);
}
