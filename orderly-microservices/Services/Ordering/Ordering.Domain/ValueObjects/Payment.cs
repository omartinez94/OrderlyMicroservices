using BuildingBlocks.Messaging.Events;

namespace Ordering.Domain.ValueObjects;

/// <summary>
/// Redacted payment summary stored on the <see cref="Models.Order"/>
/// aggregate. Matches the wire shape that
/// <c>BasketCheckoutEvent</c> v2 publishes per plan §0.4.10 —
/// discriminator + brand + last-four digits. Full PAN and CVV do
/// not enter the Ordering pipeline; they stay inside Basket.
/// </summary>
public record Payment
{
    public PaymentMethod Method { get; }
    public string Brand { get; }
    public string LastFour { get; }

    /// <summary>
    /// Sentinel instance representing deferred / unpaid orders (e.g. POS Dine-In/Takeout).
    /// Persisted with Unspecified method and "Pending" brand to satisfy non-null column constraints.
    /// </summary>
    public static readonly Payment Pending = new(PaymentMethod.Unspecified, "Pending", "0000");

    public bool IsPending => ReferenceEquals(this, Pending) || (Method == PaymentMethod.Unspecified && Brand == "Pending");

    private Payment(PaymentMethod method, string brand, string lastFour)
    {
        Method = method;
        Brand = brand;
        LastFour = lastFour;
    }

    public static Payment Of(PaymentMethod method, string brand, string lastFour)
    {
        if (method == PaymentMethod.Unspecified)
        {
            throw new DomainException("PaymentMethod.Unspecified is reserved for legacy rows; fresh orders must carry a defined method (Card / Cash / Wallet).", nameof(method));
        }
        if (string.IsNullOrWhiteSpace(brand))
        {
            throw new DomainException("Brand cannot be empty.", nameof(brand));
        }
        if (string.IsNullOrWhiteSpace(lastFour))
        {
            throw new DomainException("LastFour cannot be empty.", nameof(lastFour));
        }
        if (lastFour.Length != 4 || !lastFour.All(char.IsDigit))
        {
            throw new DomainException("LastFour must be exactly 4 digits.", nameof(lastFour));
        }

        return new Payment(method, brand, lastFour);
    }
}
