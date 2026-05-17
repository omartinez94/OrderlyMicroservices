namespace Ordering.Domain.ValueObjects;

public record Payment
{
    public string CardName { get; }
    public string CardNumber { get; }
    public string Expiration { get; }
    public string CCV { get; }
    public string PaymentMethod { get; }

    private Payment(string cardName, string cardNumber, string expiration, string ccv, string paymentMethod)
    {
        CardName = cardName;
        CardNumber = cardNumber;
        Expiration = expiration;
        CCV = ccv;
        PaymentMethod = paymentMethod;
    }

    public static Payment Of(string cardName, string cardNumber, string expiration, string ccv, string paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(cardName))
            throw new DomainException("CardName cannot be empty.", nameof(cardName));
        if (string.IsNullOrWhiteSpace(cardNumber))
            throw new DomainException("CardNumber cannot be empty.", nameof(cardNumber));
        if (string.IsNullOrWhiteSpace(expiration))
            throw new DomainException("Expiration cannot be empty.", nameof(expiration));
        if (string.IsNullOrWhiteSpace(ccv))
            throw new DomainException("CCV cannot be empty.", nameof(ccv));
        if (ccv.Length != 3)
            throw new DomainException("CCV must be 3 characters.", nameof(ccv));
        if (string.IsNullOrWhiteSpace(paymentMethod))
            throw new DomainException("PaymentMethod cannot be empty.", nameof(paymentMethod));

        return new Payment(cardName, cardNumber, expiration, ccv, paymentMethod);
    }
}
