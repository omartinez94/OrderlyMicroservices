namespace Ordering.Domain.Tests.ValueObjects;

/// <summary>
/// Covers every guard rule of <see cref="Payment.Of(string, string, string, string, string)"/>.
/// Payment is the only place card data enters the domain, so these guards are the
/// last line of defense against malformed payment values reaching an order.
/// </summary>
public sealed class PaymentTests
{
    private const string CardName = "John Doe";
    private const string CardNumber = "4111111111111111";
    private const string Expiration = "12/30";
    private const string Ccv = "123";
    private const string PaymentMethod = "CreditCard";

    /// <summary>
    /// Happy path: a fully-populated <see cref="Payment"/> round-trips all five fields
    /// unchanged so accidental field swaps surface immediately.
    /// </summary>
    [Fact]
    public void Of_WithAllValidFields_ReturnsPaymentWithSameValues()
    {
        var payment = Payment.Of(CardName, CardNumber, Expiration, Ccv, PaymentMethod);

        payment.CardName.Should().Be(CardName);
        payment.CardNumber.Should().Be(CardNumber);
        payment.Expiration.Should().Be(Expiration);
        payment.Ccv.Should().Be(Ccv);
        payment.PaymentMethod.Should().Be(PaymentMethod);
    }

    /// <summary>
    /// Verifies the "CardName cannot be empty" guard. The cardholder name is what gets
    /// printed on receipts, so a missing value should never make it past the factory.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_WithEmptyCardName_Throws(string? cardName)
    {
        Action act = () => Payment.Of(cardName, CardNumber, Expiration, Ccv, PaymentMethod);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: CardName cannot be empty. throws from Domain Layer. (Parameter: cardName)*");
    }

    /// <summary>
    /// Verifies the "CardNumber cannot be empty" guard. Note: the factory does not
    /// currently validate Luhn/checksum — that responsibility lives elsewhere.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_WithEmptyCardNumber_Throws(string? cardNumber)
    {
        Action act = () => Payment.Of(CardName, cardNumber, Expiration, Ccv, PaymentMethod);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: CardNumber cannot be empty. throws from Domain Layer. (Parameter: cardNumber)*");
    }

    /// <summary>
    /// Verifies the "Expiration cannot be empty" guard. Format validation is not enforced
    /// here (and is out of scope of this test set).
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_WithEmptyExpiration_Throws(string? expiration)
    {
        Action act = () => Payment.Of(CardName, CardNumber, expiration, Ccv, PaymentMethod);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: Expiration cannot be empty. throws from Domain Layer. (Parameter: expiration)*");
    }

    /// <summary>
    /// Verifies the "CCV cannot be empty" guard. Empty CCV is checked before the
    /// length guard, so this case does not bleed into the length-validation test.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_WithEmptyCcv_Throws(string? ccv)
    {
        Action act = () => Payment.Of(CardName, CardNumber, Expiration, ccv, PaymentMethod);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: CCV cannot be empty. throws from Domain Layer. (Parameter: ccv)*");
    }

    /// <summary>
    /// Verifies the "CCV must be 3 characters" length guard. Only non-empty inputs are
    /// used because the empty check is exercised separately — empty would hit the wrong
    /// guard. The current factory does NOT validate that CCV is numeric; if/when digit
    /// validation is added, extend the inline data with non-numeric inputs and assert
    /// the new exception message.
    /// </summary>
    [Theory]
    [InlineData("12")]
    [InlineData("1234")]
    [InlineData("1")]
    public void Of_WithCcvNotThreeChars_Throws(string ccv)
    {
        Action act = () => Payment.Of(CardName, CardNumber, Expiration, ccv, PaymentMethod);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: CCV must be 3 characters. throws from Domain Layer. (Parameter: ccv)*");
    }

    /// <summary>
    /// Verifies the "PaymentMethod cannot be empty" guard. A missing payment method
    /// would leave downstream routing logic with no signal for which processor to use.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_WithEmptyPaymentMethod_Throws(string? paymentMethod)
    {
        Action act = () => Payment.Of(CardName, CardNumber, Expiration, Ccv, paymentMethod);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: PaymentMethod cannot be empty. throws from Domain Layer. (Parameter: paymentMethod)*");
    }
}