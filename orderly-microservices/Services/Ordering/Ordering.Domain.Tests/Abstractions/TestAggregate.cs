namespace Ordering.Domain.Tests.Abstractions;

/// <summary>
/// Concrete subclass of <see cref="Aggregate{T}"/> so tests can exercise the abstract base
/// without taking a dependency on a domain entity (which would risk coupling test logic
/// to entity-specific behavior).
/// </summary>
internal sealed class TestAggregate : Aggregate<Guid>
{
    public TestAggregate() => Id = Guid.NewGuid();

    public TestAggregate(Guid id) => Id = id;
}