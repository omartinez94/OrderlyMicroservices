namespace BuildingBlocks.Messaging.Events;

public record BasketCheckoutEvent : IntegrationEvent
{
    public Guid UserId { get; init; }
    public Guid RestaurantId { get; init; }
    public List<BasketCheckoutItem> Items { get; init; } = [];
    public List<string> AppliedDiscounts { get; init; } = [];
    public decimal TotalAmount { get; init; }
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