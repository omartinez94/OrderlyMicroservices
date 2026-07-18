using BuildingBlocks.Messaging.Events;

namespace Ordering.Domain.Tests.ValueObjects;

/// <summary>
/// Covers every guard rule of <see cref="Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod, string, string)"/>.
/// Per plan §0.4.10, the Ordering pipeline carries only the redacted payment
/// summary (discriminator + brand + last-four digits). These guards are the
/// last line of defense against malformed payment values reaching an order.
/// </summary>
public sealed class PaymentTests
{
    private const PaymentMethod Method = BuildingBlocks.Messaging.Events.PaymentMethod.Card;
    private const string Brand = "Visa";
    private const string LastFour = "1111";

    /// <summary>
    /// Happy path: a fully-populated <see cref="Payment"/> round-trips all three fields
    /// unchanged so accidental field swaps surface immediately.
    /// </summary>
    [Fact]
    public void Of_WithAllValidFields_ReturnsPaymentWithSameValues()
    {
        var payment = Payment.Of(Method, Brand, LastFour);

        payment.Method.Should().Be(Method);
        payment.Brand.Should().Be(Brand);
        payment.LastFour.Should().Be(LastFour);
    }

    /// <summary>
    /// Verifies the "Brand cannot be empty" guard. The brand is what
    /// surfaces in receipts + dashboards; a missing value should
    /// never make it past the factory.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_WithEmptyBrand_Throws(string? brand)
    {
        Action act = () => Payment.Of(Method, brand, LastFour);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: Brand cannot be empty. throws from Domain Layer. (Parameter: brand)*");
    }

    /// <summary>
    /// Verifies the "LastFour cannot be empty" guard. LastFour is the
    /// canonical "card-present" surface; an empty value defeats the
    /// whole purpose of the redacted summary.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_WithEmptyLastFour_Throws(string? lastFour)
    {
        Action act = () => Payment.Of(Method, Brand, lastFour);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: LastFour cannot be empty. throws from Domain Layer. (Parameter: lastFour)*");
    }

    /// <summary>
    /// Verifies the "LastFour must be exactly 4 digits" guard. The
    /// redaction's whole point is the last four digits of the card
    /// number — any other length is malformed.
    /// </summary>
    [Theory]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("1")]
    [InlineData("abcd")]
    [InlineData("12ab")]
    public void Of_WithLastFourNotFourDigits_Throws(string lastFour)
    {
        Action act = () => Payment.Of(Method, Brand, lastFour);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: LastFour must be exactly 4 digits. throws from Domain Layer. (Parameter: lastFour)*");
    }

    /// <summary>
    /// Verifies the "PaymentMethod.Unspecified is reserved for legacy
    /// rows" guard. A fresh order must carry a defined method (Card /
    /// Cash / Wallet); the sentinel exists only for rows stamped before
    /// the v2 wire-shape rollout.
    /// </summary>
    [Fact]
    public void Of_WithUnspecifiedPaymentMethod_Throws()
    {
        Action act = () => Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Unspecified, Brand, LastFour);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: PaymentMethod.Unspecified is reserved for legacy rows; fresh orders must carry a defined method (Card / Cash / Wallet). throws from Domain Layer. (Parameter: method)*");
    }
}
