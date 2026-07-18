namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Card-payment summary carried on the wire by
/// <see cref="BasketCheckoutEvent"/> v2 (and any future event that
/// surfaces a payment).The wire carries only the
/// discriminator + brand + last-four digits — the full PAN and CVV do
/// not leave Basket's process boundary.
/// </summary>
/// <param name="Method">
/// Payment discriminator (card / cash / wallet). See
/// <see cref="PaymentMethod"/>.
/// </param>
/// <param name="Brand">
/// Card brand for <see cref="PaymentMethod.Card"/>: derived from the
/// card number's leading digit(s) ("Visa" / "Mastercard" / "Amex" /
/// "Discover" / "Unknown"). For <see cref="PaymentMethod.Cash"/>:
/// informational ("Cash"). For <see cref="PaymentMethod.Wallet"/>: the
/// wallet provider's name ("ApplePay" / "GooglePay" / ...).
/// </param>
/// <param name="LastFour">
/// Last 4 digits of the card number (card) or wallet's
/// device-account-number (wallet). For cash: <c>"0000"</c>. The
/// basket-side derivation strips non-digit characters; an empty /
/// non-numeric input yields <c>"0000"</c>.
/// </param>
public record PaymentMethodSummary(
    PaymentMethod Method,
    string Brand,
    string LastFour);
