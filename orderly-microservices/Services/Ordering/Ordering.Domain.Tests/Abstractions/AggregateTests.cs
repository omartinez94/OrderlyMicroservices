namespace Ordering.Domain.Tests.Abstractions;

/// <summary>
/// Covers <see cref="Aggregate{T}"/>'s domain-event machinery. The aggregate is the
/// unit that domain events are raised on, so its append-and-drain behavior must be
/// exact — a missed <c>ClearDomainEvents</c> would cause double-dispatch.
/// </summary>
public sealed class AggregateTests
{
    /// <summary>
    /// Default-state contract: a freshly constructed aggregate must expose an empty
    /// <c>DomainEvents</c> collection. Hangs on for null-guard regressions too.
    /// </summary>
    [Fact]
    public void DomainEvents_OnNewAggregate_IsEmpty()
    {
        var aggregate = new TestAggregate();

        aggregate.DomainEvents.Should().BeEmpty();
    }

    /// <summary>
    /// <c>AddDomainEvent</c> appends a single event to <c>DomainEvents</c> in order.
    /// </summary>
    [Fact]
    public void AddDomainEvent_AppendsToDomainEvents()
    {
        var aggregate = new TestAggregate();
        var evt = new TestDomainEvent(Guid.NewGuid());

        aggregate.AddDomainEvent(evt);

        aggregate.DomainEvents.Should().HaveCount(1);
        aggregate.DomainEvents[0].Should().BeSameAs(evt);
    }

    /// <summary>
    /// Insertion-order contract: events must surface in the order they were added so
    /// downstream dispatchers can replay them deterministically.
    /// </summary>
    [Fact]
    public void AddDomainEvent_MultipleTimes_PreservesInsertionOrder()
    {
        var aggregate = new TestAggregate();
        var first = new TestDomainEvent(Guid.NewGuid());
        var second = new TestDomainEvent(Guid.NewGuid());
        var third = new TestDomainEvent(Guid.NewGuid());

        aggregate.AddDomainEvent(first);
        aggregate.AddDomainEvent(second);
        aggregate.AddDomainEvent(third);

        aggregate.DomainEvents.Should().ContainInOrder(first, second, third);
    }

    /// <summary>
    /// <c>ClearDomainEvents</c> returns the current events AND empties the internal list
    /// so the same aggregate can accumulate new events afterwards. This is the
    /// "dispatch then drain" contract handlers depend on.
    /// </summary>
    [Fact]
    public void ClearDomainEvents_ReturnsAllEvents_AndEmptiesInternalList()
    {
        var aggregate = new TestAggregate();
        var first = new TestDomainEvent(Guid.NewGuid());
        var second = new TestDomainEvent(Guid.NewGuid());
        aggregate.AddDomainEvent(first);
        aggregate.AddDomainEvent(second);

        var cleared = aggregate.ClearDomainEvents();

        cleared.Should().HaveCount(2);
        cleared.Should().Contain(new[] { first, second });
        aggregate.DomainEvents.Should().BeEmpty();
    }

    /// <summary>
    /// <c>ClearDomainEvents</c> on a fresh aggregate must be a no-op that returns an
    /// empty array (not null) so callers can iterate the result without a null check.
    /// </summary>
    [Fact]
    public void ClearDomainEvents_OnEmptyAggregate_ReturnsEmptyArray()
    {
        var aggregate = new TestAggregate();

        var cleared = aggregate.ClearDomainEvents();

        cleared.Should().BeEmpty();
    }

    /// <summary>
    /// Documents that the aggregate can accumulate new events after a clear —
    /// the collection is reused, not replaced. A regression here would break long-lived
    /// aggregates that go through multiple save cycles.
    /// </summary>
    [Fact]
    public void ClearDomainEvents_AllowsReuse_AfterClear()
    {
        var aggregate = new TestAggregate();
        aggregate.AddDomainEvent(new TestDomainEvent(Guid.NewGuid()));
        aggregate.ClearDomainEvents();

        var next = new TestDomainEvent(Guid.NewGuid());
        aggregate.AddDomainEvent(next);

        aggregate.DomainEvents.Should().ContainSingle().Which.Should().BeSameAs(next);
    }

    /// <summary>
    /// Read-only contract: the exposed <c>DomainEvents</c> collection must be
    /// <see cref="IReadOnlyList{T}"/> so external callers cannot mutate the
    /// aggregate's internal event buffer.
    /// </summary>
    [Fact]
    public void DomainEvents_IsReadOnly_DoesNotExposeMutableList()
    {
        var aggregate = new TestAggregate();

        aggregate.DomainEvents.Should().BeAssignableTo<IReadOnlyList<IDomainEvent>>();
    }
}