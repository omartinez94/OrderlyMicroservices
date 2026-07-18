namespace Ordering.Application.Dtos;

/// <summary>
/// Redacted payment summary carried by the ordering pipeline. Matches
/// the wire shape that <c>BasketCheckoutEvent</c> v2 publishes —
/// discriminator + brand + last-four digits only. Full PAN and CVV do
/// not enter the Ordering pipeline; They stay
/// inside Basket's process boundary.
/// </summary>
public record PaymentDto(
    PaymentMethod Method,
    string Brand,
    string LastFour);