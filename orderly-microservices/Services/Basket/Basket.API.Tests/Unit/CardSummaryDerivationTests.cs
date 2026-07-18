using BuildingBlocks.Messaging.Events;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Unit-level coverage for <see cref="CardSummaryDerivation"/>. Locks
/// the §0.4.10 wire-shape redaction: the basket-side handler turns
/// the raw card number into a brand + last-four-digits pair before
/// publishing <c>BasketCheckoutEvent</c> v2. Brand is derived from the
/// card's leading digit (ISO/IEC 7812 issuer IDs); last-four is the
/// trailing 4 digits with non-digit characters stripped.
/// </summary>
public sealed class CardSummaryDerivationTests
{
    [Theory]
    [InlineData("4111111111111111", "Visa")]
    [InlineData("4012888888881881", "Visa")]
    [InlineData("5500000000000004", "Mastercard")]
    [InlineData("5105105105105100", "Mastercard")]
    [InlineData("2223000048400011", "Mastercard")] // 2-series BIN range (post-2017)
    [InlineData("340000000000009", "Amex")]
    [InlineData("370000000000002", "Amex")]
    [InlineData("6011111111111117", "Discover")]
    public void DeriveCardBrand_RecognisesLeadingDigitIssuer(string cardNumber, string expectedBrand)
    {
        CardSummaryDerivation.DeriveCardBrand(cardNumber).Should().Be(expectedBrand);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0000")]
    [InlineData("9999999999999999")] // unrecognised leading digit
    public void DeriveCardBrand_EmptyOrUnrecognised_ReturnsUnknown(string? cardNumber)
    {
        CardSummaryDerivation.DeriveCardBrand(cardNumber).Should().Be("Unknown");
    }

    [Theory]
    [InlineData("4111111111111111", "1111")]
    [InlineData("5500000000000004", "0004")]
    [InlineData("340000000000009", "0009")]
    [InlineData("6011111111111117", "1117")]
    public void ExtractLastFour_ReturnsTrailingFourDigits(string cardNumber, string expected)
    {
        CardSummaryDerivation.ExtractLastFour(cardNumber).Should().Be(expected);
    }

    [Theory]
    [InlineData("4111-1111-1111-1111", "1111")] // dashes stripped
    [InlineData("4111 1111 1111 1111", "1111")] // spaces stripped
    [InlineData("41111111111111112", "1112")]   // 17 digits → last 4
    public void ExtractLastFour_StripsNonDigitCharacters(string cardNumber, string expected)
    {
        CardSummaryDerivation.ExtractLastFour(cardNumber).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")] // fewer than 4 digits after digit-only filter
    [InlineData("abc")]
    public void ExtractLastFour_EmptyOrTooShort_Returns0000(string? cardNumber)
    {
        CardSummaryDerivation.ExtractLastFour(cardNumber).Should().Be("0000");
    }

    [Fact]
    public void EndToEnd_RedactionPipeline_ProducesExpectedSummary()
    {
        // Locks the full pipeline: a real card number → brand + last-four
        // → PaymentMethodSummary. This is the exact shape BasketCheckoutEvent
        // v2 carries on the wire.
        var cardNumber = "4111111111111111";
        var summary = new PaymentMethodSummary(
            Method: PaymentMethod.Card,
            Brand: CardSummaryDerivation.DeriveCardBrand(cardNumber),
            LastFour: CardSummaryDerivation.ExtractLastFour(cardNumber));

        summary.Method.Should().Be(PaymentMethod.Card);
        summary.Brand.Should().Be("Visa");
        summary.LastFour.Should().Be("1111");
    }
}
