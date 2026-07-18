namespace BuildingBlocks.Messaging.Events;

public record BasketCheckoutEvent : IntegrationEvent
{
    /// <summary>
    /// Wire-format version. v2 drops the raw card fields
    /// (<c>CardName</c>, <c>CardNumber</c>, <c>Expiration</c>,
    /// <c>CVV</c>, <c>PaymentMethod</c> string) and replaces them with
    /// <see cref="PaymentMethodSummary"/>. Consumers MUST be on the v2
    /// shape before this event is published — see plan §6 Phase 2 2.1.
    /// </summary>
    public override int MessageVersion { get; init; } = 2;

    public Guid UserId { get; init; }
    public Guid RestaurantId { get; init; }
    public List<BasketCheckoutItem> Items { get; init; } = [];
    public List<string> AppliedDiscounts { get; init; } = [];
    public decimal TotalAmount { get; init; }

    // Shipping and Billing Address
    public string FirstName { get; init; } = default!;
    public string LastName { get; init; } = default!;
    public string EmailAddress { get; init; } = default!;
    public string AddressLine { get; init; } = default!;
    public string Country { get; init; } = default!;
    public string State { get; init; } = default!;
    public string City { get; init; } = default!;
    public string ZipCode { get; init; } = default!;

    /// <summary>
    /// Redacted payment summary on the wire. Replaces the v1 raw
    /// card fields; the full PAN and CVV stay inside Basket's
    /// process boundary. Defaulted to <c>null</c> so JSON readers
    /// without the v2 contract see a missing field rather than a
    /// confusing default.
    /// </summary>
    public PaymentMethodSummary? PaymentMethodSummary { get; init; }
}


public class BasketCheckoutItem
{
    public int MenuItemId { get; init; }
    public string Name { get; init; } = default!;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public List<BasketItemVariationDto> Variations { get; init; } = [];
    public List<BasketItemCustomizationDto> Customizations { get; init; } = [];
    public decimal TotalPrice { get; init; }
}

public class BasketItemVariationDto
{
    public string Name { get; init; } = default!;
    public string Value { get; init; } = default!;
    public decimal Price { get; init; }
}

public class BasketItemCustomizationDto
{
    public string Ingredient { get; init; } = default!;
    public string Action { get; init; } = default!;
}