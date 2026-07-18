using BuildingBlocks.Messaging.Events;

namespace Basket.API.Dtos;

public class BasketCheckoutDto
{
    public Guid UserId { get; set; }
    public Guid RestaurantId { get; set; }

    // Shipping and Billing Address
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public string EmailAddress { get; set; } = default!;
    public string AddressLine { get; set; } = default!;
    public string Country { get; set; } = default!;
    public string State { get; set; } = default!;
    public string ZipCode { get; set; } = default!;

    // Payment — these raw fields stay on the DTO for the v1 integration
    // window (clients still send them; the validator runs server-side
    // Luhn + regex checks). The CheckoutBasketHandler reads them to
    // build PaymentMethodSummary; the wire event payload carries only
    // the summary (discriminator + brand + last-four). The raw fields
    // never leave Basket's process boundary.
    public string CardName { get; set; } = default!;
    public string CardNumber { get; set; } = default!;
    public string Expiration { get; set; } = default!;
    public string CVV { get; set; } = default!;
    public PaymentMethod PaymentMethod { get; set; }
}
