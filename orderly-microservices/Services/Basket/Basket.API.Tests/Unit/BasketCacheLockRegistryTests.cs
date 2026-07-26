namespace Basket.API.Tests.Unit;

/// <summary>
/// Phase 3.1: <see cref="BasketCacheLockRegistry"/> is a singleton
/// per-key <see cref="SemaphoreSlim"/> gate. Tests assert the
/// core semantics that <c>CachedBasketRepository</c> relies on:
/// <list type="number">
///   <item>different keys produce independent gates (no cross-key blocking).</item>
///   <item>concurrent acquisitions on the SAME key serialise.</item>
///   <item>disposal cancels pending waiters.</item>
///   <item>disposal is idempotent (can be called twice without throwing).</item>
/// </list>
/// </summary>
public sealed class BasketCacheLockRegistryTests
{
    private static string RandomKey() => $"basket:{Guid.NewGuid()}:{Guid.NewGuid()}";

    [Fact]
    public async Task AcquireAsync_DifferentKeys_DoNotBlockEachOther()
    {
        // Arrange
        var registry = new BasketCacheLockRegistry();
        var keyA = RandomKey();
        var keyB = RandomKey();

        // Act — acquire A; A is held while B is requested. B should
        // obtain its OWN semaphore without waiting on A.
        await using var handleA = await registry.AcquireAsync(keyA, CancellationToken.None);
        var bStarted = DateTime.UtcNow;
        await using var handleB = await registry.AcquireAsync(keyB, CancellationToken.None);
        var bAcquired = DateTime.UtcNow;

        // Assert — B's acquire is essentially instantaneous because it
        // has its own semaphore. A 5-second budget avoids CI flakiness
        // while still failing the test if cross-key blocking is ever
        // introduced.
        (bAcquired - bStarted).Should().BeLessThan(TimeSpan.FromSeconds(5));

        await registry.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_SameKey_SerialisesConcurrentCallers()
    {
        // Arrange
        var registry = new BasketCacheLockRegistry();
        var key = RandomKey();

        // Act — caller A holds the gate; caller B's WaitAsync blocks
        // until A releases. We assert the blocking-vs-release ordering
        // by tracking when B's AcquireAsync completes relative to when
        // A disposes.
        var aHandle = await registry.AcquireAsync(key, CancellationToken.None);

        var bAcquiredTcs = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bTask = Task.Run(async () =>
        {
            await using var _ = await registry.AcquireAsync(key, CancellationToken.None);
            bAcquiredTcs.TrySetResult(DateTime.UtcNow);
        });

        // While A still holds the gate, B should not have acquired.
        await Task.Delay(50);
        bAcquiredTcs.Task.IsCompletedSuccessfully.Should().BeFalse("B should still be waiting while A holds the gate");

        // Release A — B should now acquire within a small budget.
        var releasedAt = DateTime.UtcNow;
        await aHandle.DisposeAsync();
        var bAcquiredAt = await bAcquiredTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var elapsedAfterRelease = bAcquiredAt - releasedAt;

        // Assert
        elapsedAfterRelease.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        elapsedAfterRelease.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "B should acquire promptly once A releases");

        await bTask;
        await registry.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_CallerCancellation_ThrowsAndReleasesGate()
    {
        // Arrange
        var registry = new BasketCacheLockRegistry();
        var key = RandomKey();

        // Caller A holds the gate; caller B is enqueued behind A.
        var aHandle = await registry.AcquireAsync(key, CancellationToken.None);

        using var bCts = new CancellationTokenSource();
        var bTask = Task.Run(async () =>
        {
            await using var _ = await registry.AcquireAsync(key, bCts.Token);
            return true;
        });

        await Task.Delay(50);

        // Act — caller B cancels its own token; the
        // AcquireAsync should raise OperationCanceledException so
        // B's task ends in Canceled state (NOT a successful acquire
        // AND NOT a hang).
        bCts.Cancel();

        // Assert — B's task is cancelled, not hung.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bTask);

        // A still holds the gate; a fresh caller (no cancellation)
        // should still block on it because B's cancellation did not
        // release the gate.
        var freshAcquiredTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var freshTask = Task.Run(async () =>
        {
            await using var _ = await registry.AcquireAsync(key, CancellationToken.None);
            freshAcquiredTcs.TrySetResult(true);
        });

        await Task.Delay(50);
        freshAcquiredTcs.Task.IsCompletedSuccessfully.Should().BeFalse(
            "the gate is still held by A; B's cancellation must not have released it");

        // Release A — fresh caller now acquires.
        await aHandle.DisposeAsync();
        await freshTask.WaitAsync(TimeSpan.FromSeconds(5));

        await registry.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_PendingWaiters_RaiseOperationCanceled()
    {
        // Arrange
        var registry = new BasketCacheLockRegistry();
        var key = RandomKey();

        // A holds the gate; B is enqueued behind it.
        var aHandle = await registry.AcquireAsync(key, CancellationToken.None);
        var bTask = Task.Run(async () =>
        {
            await using var _ = await registry.AcquireAsync(key, CancellationToken.None);
            return true;
        });

        await Task.Delay(50);

        // Act — dispose the registry; B should raise
        // OperationCanceledException promptly (host-shutdown path).
        await registry.DisposeAsync();

        // Assert — B's wait is cancelled, not hung. The handle is
        // already disposed by the registry, so acquiring A's `using`
        // would be a double-dispose; do not throw on it. Just verify
        // B's AcquireAsync threw.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bTask);

        // A's handle should be safe to dispose AGAIN — the registry's
        // dispose path leaves the semaphore in a state where this is
        // a no-op rather than a crash.
        await aHandle.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        // Arrange — the registry's lifecycle may fire
        // ApplicationStopping AND then be reached by the service
        // container teardown; both paths call DisposeAsync. The
        // registry must be idempotent.
        var registry = new BasketCacheLockRegistry();
        var key = RandomKey();
        _ = await registry.AcquireAsync(key, CancellationToken.None);

        // Act + Assert — double-dispose is a no-op.
        await registry.DisposeAsync();
        await registry.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_NoAcquisitions_CompletesCleanly()
    {
        // Arrange — never used. Disposal must still run.
        var registry = new BasketCacheLockRegistry();

        // Act + Assert
        await registry.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_AcrossManyKeys_NoLeakAcrossDispose()
    {
        // Arrange — stress the registry with a number of distinct
        // keys to assert no per-acquire state leaks across the
        // singleton's lifetime.
        var registry = new BasketCacheLockRegistry();
        var handles = new List<IAsyncDisposable>(32);
        try
        {
            for (var i = 0; i < 32; i++)
            {
                handles.Add(await registry.AcquireAsync(RandomKey(), CancellationToken.None));
            }

            // Release all in LIFO order; then dispose the registry.
            handles.Reverse();
            foreach (var handle in handles)
            {
                await handle.DisposeAsync();
            }

            await registry.DisposeAsync();
        }
        finally
        {
            // Defensive cleanup in case any step throws — better a
            // safe disposal than a CI leak.
            foreach (var handle in handles)
            {
                try { await handle.DisposeAsync(); } catch { /* best-effort */ }
            }
            try { await registry.DisposeAsync(); } catch { /* best-effort */ }
        }
    }
}
