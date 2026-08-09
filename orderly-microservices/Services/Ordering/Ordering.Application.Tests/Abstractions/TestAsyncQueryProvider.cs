using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace Ordering.Application.Tests.Abstractions;

/// <summary>
/// Adapter <see cref="IAsyncQueryProvider"/> that wraps an in-memory
/// <see cref="IQueryProvider"/> (typically <see cref="EnumerableQuery{T}"/>'s
/// provider) and routes EF Core's <c>ExecuteAsync&lt;Task&lt;T&gt;&gt;</c>
/// through it. Synchronous <c>Execute</c> / <c>CreateQuery</c> delegate
/// directly; the async wrapper evaluates the expression synchronously and
/// wraps the result in <see cref="Task.FromResult{TResult}(TResult)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scott Nichols / MSDN canonical pattern — used by EF Core unit-testing
/// libraries (e.g. <c>EntityFrameworkCore.Testing</c>) for the same purpose.
/// Implemented in-tree to honor <c>.agents/plan/testing/E2E_TEST_COVERAGE_PLAN.md</c>
/// §0.2 "no new NuGet packages".
/// </para>
/// <para>
/// Only the <c>Task&lt;T&gt;</c> shape is unwrapped. <c>ValueTask&lt;T&gt;</c>
/// is also supported. Other shapes fall through to <c>Execute</c> on the
/// inner provider, which will throw if the runtime shape is unexpected.
/// </para>
/// </remarks>
internal sealed class TestAsyncQueryProvider : IAsyncQueryProvider
{
    private readonly IQueryProvider _inner;

    internal TestAsyncQueryProvider(IQueryProvider inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    // -------- IQueryProvider --------

    public IQueryable CreateQuery(Expression expression)
    {
        var query = _inner.CreateQuery(expression);
        return new TestAsyncEnumerable<object>(query.Cast<object>().ToList());
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        var query = _inner.CreateQuery<TElement>(expression);
        return new TestAsyncEnumerable<TElement>(query.ToList());
    }

    public object? Execute(Expression expression) => _inner.Execute(expression);

    public TResult? Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

    // -------- IAsyncQueryProvider --------

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        var resultType = typeof(TResult);

        // Task<T> — the common EF Core async shape (ToListAsync, SingleOrDefaultAsync, LongCountAsync).
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var innerType = resultType.GetGenericArguments()[0];
            var value = _inner.Execute(Expression.Convert(expression, innerType));
            var fromResult = typeof(Task)
                .GetMethod(nameof(Task.FromResult), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(innerType);
            return (TResult)fromResult.Invoke(null, new object?[] { value })!;
        }

        // ValueTask<T> — EF Core's ExecuteDeleteAsync/ExecuteUpdateAsync and a few newer shapes.
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var innerType = resultType.GetGenericArguments()[0];
            var value = _inner.Execute(Expression.Convert(expression, innerType));
            var valueTaskCtor = resultType.GetConstructor(new[] { innerType })!;
            return (TResult)valueTaskCtor.Invoke(new object?[] { value });
        }

        // Plain sync execution — caller asked for a non-task shape; defer to inner.
        return (TResult)_inner.Execute(expression)!;
    }
}
