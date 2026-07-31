using System.Collections.Concurrent;

namespace Basket.API.Caching;

/// <inheritdoc cref="IBasketCacheLockRegistry" />
/// <remarks>
/// <para>
/// Per-key <see cref="SemaphoreSlim"/>(1, 1) coalesces concurrent
/// "cache-miss, go to DB" callers into a single inner-repository query.
/// The semaphore is created on first <see cref="AcquireAsync"/> for a
/// key and re-used thereafter; <see cref="IAsyncDisposable.DisposeAsync"/>
/// cancels pending waiters and disposes every semaphore the registry
/// ever created.
/// </para>
/// <para>
/// The <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey, Func{TKey, TValue})"/>
/// factory delegate is intentionally lock-free — two concurrent
/// first-acquire calls may BOTH run the factory (creating two
/// semaphores), but only one ends up in the dictionary; the second
/// caller throws its copy away and uses the winner's. Lock-free
/// lookup-by-key from the dictionary is the steady-state path.
/// </para>
/// </remarks>
public sealed class BasketCacheLockRegistry : IBasketCacheLockRegistry, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stoppingCts = new();
    private int _disposed; // 0 = live, 1 = disposed; Interlocked-only.

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> AcquireAsync(string cacheKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheKey);

        // Single-flight per (cacheKey): all concurrent cache misses on
        // the same key collapse onto one inner-repository query. The
        // semaphore stays cached for the lifetime of the host, so the
        // 2nd-and-later acquisitions reuse it (no per-key setup cost).
        var semaphore = _locks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));

        // Race cancellation: caller-supplied token + host-shutdown.
        // Either fires → the WaitAsync throws → the awaited using
        // block exits without ever entering the critical section.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _stoppingCts.Token);

        try
        {
            await semaphore.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // If shutdown fired and the semaphore is held by a still-
            // running caller, the next caller will be cancelled here
            // and never hold the gate — the registry doesn't need to
            // release anything on its own behalf.
            throw;
        }

        return new Releaser(semaphore);
    }

    public async ValueTask DisposeAsync()
    {
        // Idempotent dispose. The host's
        // IHostApplicationLifetime.ApplicationStopping callback AND
        // the service-container's teardown may both call DisposeAsync
        // during graceful shutdown — honour both without throwing.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Stop accepting new waiters. Pending WaitAsync callers
        // observe cancellation via the linked CTS raised at
        // AcquireAsync time, and throw OperationCanceledException
        // immediately. This is the contract documented in plan
        // "cancels any pending WaitAsync callers with
        // OperationCanceledException". The currently-held semaphore
        // owner (the one inside its own critical section) is NOT
        // affected — its work proceeds until the host's
        // IHostOptions.ShutdownTimeout bounds it.
        //
        // Note: we deliberately do NOT call semaphore.Release() here.
        // Doing so would race with the cancellation: Release()
        // succeeds synchronously, increments the semaphore count, and
        // the pending waiter observes the gate as "free" rather than
        // "cancelled" — defeating the plan's contract. By cancelling
        // the linked token alone, the waiter throws
        // OperationCanceledException regardless of gate state.
        _stoppingCts.Cancel();

        // Yield back to the scheduler so that pending WaitAsync callers
        // on threadpool threads can observe the linked-CTS cancellation
        // and throw OperationCanceledException BEFORE we dispose the
        // semaphores underneath them. Without this yield, semaphore
        // disposal can race ahead of the cancellation callback,
        // leaving the WaitAsync in a state where it never completes
        // (the internal wait handle is torn down before the
        // cancellation fires).
        await Task.Yield();

        // Now safe to dispose the CTS — all linked tokens have already
        // observed the cancellation signal above.
        _stoppingCts.Dispose();

        // Dispose every semaphore we ever created. SemaphoreSlim only
        // implements IDisposable (synchronous) — the kernel-handle
        // release is microseconds and the host's ShutdownTimeout grace
        // (default 30 s) accommodates it. A holder currently inside
        // its critical section will see ObjectDisposedException on
        // its Releaser.DisposeAsync() (caught silently there). If the
        // kernel semaphore is in use by another thread at dispose
        // time, the .NET 6+ implementation tolerates this; older
        // runtimes may produce undefined behaviour, but the policy
        // here accepts that race rather than block shutdown.
        foreach (var semaphore in _locks.Values)
        {
            try
            {
                semaphore.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Concurrent teardown — already gone.
            }
        }
    }

    /// <summary>
    /// Handle returned by <see cref="AcquireAsync"/> — releasing the
    /// semaphore on <see cref="IAsyncDisposable.DisposeAsync"/> so the
    /// next waiter proceeds.
    /// </summary>
    /// <remarks>
    /// The semaphore is shared across callers, so disposing this
    /// handle does NOT remove the entry from the registry — the
    /// registry owns the entry for the host's lifetime and disposes
    /// it in <see cref="DisposeAsync"/>. Disposing here is a release,
    /// not a removal.
    /// </remarks>
    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            try
            {
                semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // Defensive: a release on a semaphore at its initial
                // count is a logic bug; under single-flight it
                // shouldn't happen. Swallow rather than crash the host.
            }
            catch (ObjectDisposedException)
            {
                // The registry is mid-dispose; the semaphore has
                // already been torn down. Ignore — the request is
                // already on its way out.
            }
            return ValueTask.CompletedTask;
        }
    }
}
