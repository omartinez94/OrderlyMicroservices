using System.Collections;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Ordering.Application.Tests.Abstractions;

/// <summary>
/// Test-only <see cref="DbSet{TEntity}"/> that wraps an in-memory
/// <see cref="IEnumerable{T}"/> seed and exposes it through the
/// <c>IQueryable&lt;T&gt;</c> interface — specifically
/// <see cref="IQueryable.Provider"/> is set to a
/// <see cref="TestAsyncQueryProvider"/> that wraps the in-memory provider,
/// so EF Core's async LINQ extensions (<c>ToListAsync</c>,
/// <c>SingleOrDefaultAsync</c>, <c>LongCountAsync</c>) work without an
/// EF Core provider or MockQueryable dependency.
/// </summary>
/// <remarks>
/// <para>
/// Subclasses <see cref="DbSet{TEntity}"/> directly. In EF Core 10,
/// <c>DbSet&lt;T&gt;</c> implements the non-generic <see cref="IQueryable"/>
/// interface (NOT <c>IQueryable&lt;T&gt;</c>); the three members are
/// inherited from the interface but are not declared on the class as
/// virtual or hidden members. We override via explicit interface
/// implementation on <see cref="IQueryable"/> (non-generic) — interface
/// dispatch picks the most-derived explicit implementation.
/// </para>
/// <para>
/// EF Core's async extension methods check <c>source.Provider is
/// IAsyncQueryProvider</c> and route through
/// <c>ExecuteAsync&lt;Task&lt;T&gt;&gt;</c>; they do NOT consume
/// <see cref="IAsyncEnumerable{T}"/>. So we only need to wire the
/// <c>IQueryable</c> interface — no <c>IAsyncEnumerable</c> required.
/// </para>
/// <para>
/// <b>Caller contract</b>: <c>Include</c>, <c>AsNoTracking</c>,
/// <c>Skip</c>/<c>Take</c> are no-ops on this provider. Test authors must
/// pre-populate aggregates with their navigation collections (e.g. seed an
/// <c>Order</c> by calling <c>Order.Add(menuItemId, qty, price)</c> so the
/// <c>OrderItems</c> collection is populated before the query runs).
/// </para>
/// </remarks>
public sealed class MockDbSet<TEntity> : DbSet<TEntity>, System.Linq.IQueryable
    where TEntity : class
{
    private readonly IQueryable<TEntity> _inner;

    internal MockDbSet(IEnumerable<TEntity> seed)
    {
        _inner = seed.AsQueryable();
    }

    /// <summary>
    /// Build a <see cref="MockDbSet{TEntity}"/> wrapping <paramref name="seed"/>.
    /// </summary>
    public static MockDbSet<TEntity> From(IEnumerable<TEntity> seed)
        => new(seed ?? throw new ArgumentNullException(nameof(seed)));

    // ---- Explicit IQueryable (non-generic) implementations ----
    // DbSet<T> implements IQueryable (non-generic) — the three members
    // Provider / Expression / ElementType are NOT virtual on the class, so
    // we cannot override them. Explicit interface implementation is the
    // only mechanism that interface dispatch will pick up.

    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider(_inner.Provider);

    Expression IQueryable.Expression => _inner.Expression;

    Type IQueryable.ElementType => _inner.ElementType;

    IEnumerator IEnumerable.GetEnumerator() => _inner.GetEnumerator();

    /// <summary>Required abstract member from <see cref="DbSet{TEntity}"/>. Not exercised by query-handler tests.</summary>
    public override Microsoft.EntityFrameworkCore.Metadata.IEntityType EntityType
        => throw new NotSupportedException(
            "MockDbSet is for query-handler unit tests; EntityType is not exercised.");
}
