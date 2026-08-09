using System.Collections;
using System.Linq.Expressions;

namespace Ordering.Application.Tests.Abstractions;

/// <summary>
/// In-memory backing for <see cref="MockDbSet{TEntity}"/>. Implements the
/// dual interfaces (<c>IQueryable&lt;T&gt;</c> + <c>IAsyncEnumerable&lt;T&gt;</c>)
/// that EF Core's async LINQ extensions need: <c>ToListAsync</c>,
/// <c>SingleOrDefaultAsync</c>, <c>LongCountAsync</c> route through
/// <see cref="TestAsyncQueryProvider"/>; the synchronous LINQ chain
/// (<c>Where</c>, <c>OrderBy</c>, <c>Skip</c>, <c>Take</c>, <c>Include</c>,
/// <c>AsNoTracking</c>) routes through <c>EnumerableQuery.Provider</c>.
/// </summary>
/// <remarks>
/// <para>
/// Pattern is the canonical Scott Nichols / MSDN approach to mocking
/// <see cref="Microsoft.EntityFrameworkCore.DbSet{TEntity}"/> with async
/// query support — no <c>MockQueryable.NSubstitute</c> / EF InMemory
/// dependency required, honoring the plan's "no new NuGet packages"
/// guardrail at <c>.agents/plan/testing/E2E_TEST_COVERAGE_PLAN.md</c> §0.2.
/// </para>
/// <para>
/// <b>Caller contract</b>: <c>Include</c>, <c>AsNoTracking</c>,
/// <c>Skip</c>/<c>Take</c> are no-ops on this provider. Test authors must
/// pre-populate aggregates with their navigation collections (e.g. seed an
/// <c>Order</c> by calling <c>Order.Add(menuItemId, qty, price)</c> so the
/// <c>OrderItems</c> collection is populated before the query runs).
/// </para>
/// </remarks>
internal sealed class TestAsyncEnumerable<T> : IAsyncEnumerable<T>, IQueryable<T>, IOrderedQueryable<T>, IEnumerable<T>
{
    private readonly List<T> _data;

    internal TestAsyncEnumerable(IEnumerable<T> data)
    {
        _data = data?.ToList() ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>
    /// Backing in-memory queryable — its <c>Provider</c> evaluates LINQ
    /// expressions synchronously against <see cref="_data"/>.
    /// </summary>
    private IQueryable<T> Self => _data.AsQueryable();

    public Type ElementType => Self.ElementType;

    public Expression Expression => Self.Expression;

    public IQueryProvider Provider => new TestAsyncQueryProvider(Self.Provider);

    public IEnumerator<T> GetEnumerator() => _data.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new TestAsyncEnumerator<T>(_data.GetEnumerator());
}

/// <summary>
/// Adapter that turns an <see cref="IEnumerator{T}"/> into an
/// <see cref="IAsyncEnumerator{T}"/> — used by <see cref="TestAsyncEnumerable{T}"/>.
/// </summary>
internal sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
{
    private readonly IEnumerator<T> _inner;

    internal TestAsyncEnumerator(IEnumerator<T> inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public T Current => _inner.Current;

    public ValueTask DisposeAsync()
    {
        _inner.Dispose();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> MoveNextAsync() => new(_inner.MoveNext());
}
