namespace BuildingBlocks.Messaging.Events;

public record BasketCheckoutEvent : IntegrationEvent
{
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

    // Payment
    public string CardName { get; init; } = default!;
    public string CardNumber { get; init; } = default!;
    public string Expiration { get; init; } = default!;
    public string CVV { get; init; } = default!;
    public string PaymentMethod { get; init; } = default!;
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