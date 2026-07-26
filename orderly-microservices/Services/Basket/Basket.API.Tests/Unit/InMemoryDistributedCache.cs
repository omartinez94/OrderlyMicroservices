using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;

namespace Basket.API.Tests.Unit;

/// <summary>
/// In-memory <see cref="IDistributedCache"/> for unit tests.
/// Bypasses NSubstitute's "extension method mocking" issue (the
/// <c>GetStringAsync</c> / <c>SetStringAsync</c> helpers are
/// extension methods defined in
/// <c>Microsoft.Extensions.Caching.Distributed.DistributedCacheExtensions</c>,
/// which NSubstitute cannot intercept on a substituted interface).
/// </summary>
/// <remarks>
/// Thread-safety: the underlying <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// is safe under concurrent reads/writes — the test scenarios for
/// Phase 3.1 (single-flight contention) rely on this. Production code
/// never calls this fake; the real path goes through
/// <c>Microsoft.Extensions.Caching.StackExchangeRedis</c>.
/// </remarks>
internal sealed class InMemoryDistributedCache : IDistributedCache
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, byte[]> Snapshot => _store;

    public int Count => _store.Count;

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
        Task.FromResult(_store.TryGetValue(key, out var bytes) ? bytes : null);

    public Task SetAsync(string key, byte[]? value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        if (value is not null)
        {
            _store[key] = value;
        }
        else
        {
            _store.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    // Sync members exist on the interface for legacy callers but the
    // production code only calls the async API. Concrete implementations
    // are required by the interface.
    public byte[]? Get(string key) => _store.TryGetValue(key, out var bytes) ? bytes : null;
    public void Set(string key, byte[]? value, DistributedCacheEntryOptions options)
    {
        if (value is not null) _store[key] = value;
        else _store.TryRemove(key, out _);
    }
    public void Refresh(string key) { /* no-op */ }
    public void Remove(string key) => _store.TryRemove(key, out _);
}
