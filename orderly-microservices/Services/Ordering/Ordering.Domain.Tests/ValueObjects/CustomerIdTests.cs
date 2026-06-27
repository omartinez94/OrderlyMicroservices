namespace Ordering.Domain.Tests.ValueObjects;

/// <summary>
/// Covers the strongly-typed <see cref="CustomerId"/> wrapper. The wrapper exists to
/// prevent the silent use of <see cref="Guid.Empty"/> as a customer identifier, which
/// is a common source of "phantom" customer rows in event stores.
/// </summary>
public sealed class CustomerIdTests
{
    /// <summary>
    /// Happy path: any non-empty Guid round-trips through the wrapper.
    /// </summary>
    [Fact]
    public void Of_WithNonEmptyGuid_ReturnsGuid()
    {
        var guid = Guid.NewGuid();

        var customerId = CustomerId.Of(guid);

        customerId.Value.Should().Be(guid);
    }

    /// <summary>
    /// <see cref="Guid.Empty"/> is rejected. This is the only invariant the wrapper
    /// enforces — if it were ever relaxed, every "find customer by id" lookup would
    /// start returning the empty customer as a valid result.
    /// </summary>
    [Fact]
    public void Of_WithEmptyGuid_Throws()
    {
        Action act = () => CustomerId.Of(Guid.Empty);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: CustomerId cannot be empty. throws from Domain Layer. (Parameter: value)*");
    }
}