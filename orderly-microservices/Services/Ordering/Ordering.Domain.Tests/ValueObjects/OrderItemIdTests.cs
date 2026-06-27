namespace Ordering.Domain.Tests.ValueObjects;

/// <summary>
/// Covers the strongly-typed <see cref="OrderItemId"/> wrapper. Each line item on an
/// order needs a unique identifier so prep state can be updated independently.
/// </summary>
public sealed class OrderItemIdTests
{
    /// <summary>
    /// Happy path: any non-empty Guid round-trips through the wrapper.
    /// </summary>
    [Fact]
    public void Of_WithNonEmptyGuid_ReturnsGuid()
    {
        var guid = Guid.NewGuid();

        var orderItemId = OrderItemId.Of(guid);

        orderItemId.Value.Should().Be(guid);
    }

    /// <summary>
    /// <see cref="Guid.Empty"/> is rejected. Two empty-id line items on the same order
    /// would collide in lookups by id.
    /// </summary>
    [Fact]
    public void Of_WithEmptyGuid_Throws()
    {
        Action act = () => OrderItemId.Of(Guid.Empty);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: OrderItemId cannot be empty. throws from Domain Layer. (Parameter: value)*");
    }
}