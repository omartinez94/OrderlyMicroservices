namespace Ordering.Domain.Tests.ValueObjects;

/// <summary>
/// Covers the single guard rule of <see cref="OrderNumber.Of"/>: the supplied string
/// must not be null, empty, or whitespace. Order numbers are user-facing references
/// (printed on receipts, used in support searches) so an empty value must never escape.
/// </summary>
public sealed class OrderNumberTests
{
    /// <summary>
    /// Happy path: a non-empty value round-trips unchanged.
    /// </summary>
    [Fact]
    public void Of_WithNonEmptyValue_ReturnsValue()
    {
        var orderNumber = OrderNumber.Of("ORD-2026-0001");

        orderNumber.Value.Should().Be("ORD-2026-0001");
    }

    /// <summary>
    /// Null is rejected with the documented <see cref="DomainException"/> message —
    /// the value-objects record constructor is private, so the factory is the only
    /// entry point and must guard against null.
    /// </summary>
    [Fact]
    public void Of_WithNull_Throws()
    {
        Action act = () => OrderNumber.Of(null!);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: OrderNumber cannot be empty. throws from Domain Layer. (Parameter: value)*");
    }

    /// <summary>
    /// Empty string is rejected identically to null.
    /// </summary>
    [Fact]
    public void Of_WithEmpty_Throws()
    {
        Action act = () => OrderNumber.Of(string.Empty);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: OrderNumber cannot be empty. throws from Domain Layer. (Parameter: value)*");
    }

    /// <summary>
    /// Whitespace-only strings are also rejected — defends against trim() being
    /// forgotten upstream and an " " leaking through.
    /// </summary>
    [Fact]
    public void Of_WithWhitespace_Throws()
    {
        Action act = () => OrderNumber.Of("   ");

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: OrderNumber cannot be empty. throws from Domain Layer. (Parameter: value)*");
    }
}