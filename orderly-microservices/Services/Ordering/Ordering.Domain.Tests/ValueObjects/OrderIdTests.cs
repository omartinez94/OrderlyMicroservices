namespace Ordering.Domain.Tests.ValueObjects;

/// <summary>
/// Covers the strongly-typed <see cref="OrderId"/> wrapper. Used as the order's primary
/// key throughout the domain — an empty id would collapse orders together on lookup.
/// </summary>
public sealed class OrderIdTests
{
    /// <summary>
    /// Happy path: any non-empty Guid round-trips through the wrapper.
    /// </summary>
    [Fact]
    public void Of_WithNonEmptyGuid_ReturnsGuid()
    {
        var guid = Guid.NewGuid();

        var orderId = OrderId.Of(guid);

        orderId.Value.Should().Be(guid);
    }

    /// <summary>
    /// <see cref="Guid.Empty"/> is rejected. <c>OrderId</c> is referenced by
    /// <c>OrderItem</c>, <c>OrderBill</c>, and reservation/event payloads, so a
    /// permissive guard here would cascade into many downstream bugs.
    /// </summary>
    [Fact]
    public void Of_WithEmptyGuid_Throws()
    {
        Action act = () => OrderId.Of(Guid.Empty);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: OrderId cannot be empty. throws from Domain Layer. (Parameter: value)*");
    }
}