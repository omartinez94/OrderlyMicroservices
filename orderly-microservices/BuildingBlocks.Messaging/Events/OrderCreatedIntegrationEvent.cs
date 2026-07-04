namespace BuildingBlocks.Messaging.Events;

/// <summary>
/// Published by Ordering when an <c>Order</c> is created. Consumed by Kitchen to
/// build a <c>KitchenTicket</c> projection. Carries NO payment data — Ordering
/// keeps that internal and never publishes it on the bus.
/// </summary>
public record OrderCreatedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = default!;
    public Guid RestaurantId { get; init; }
    public Guid? TableId { get; init; }
    public int OrderType { get; init; }
    public Guid CustomerId { get; init; }

    // Financials (no PaymentDto, no Card* fields)
    public decimal Subtotal { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal DiscountAmount { get; init; }
    public string Currency { get; init; } = default!;
    public string? DiscountCode { get; init; }

    public OrderAddress BillingAddress { get; init; } = default!;
    public OrderAddress? DeliveryAddress { get; init; }

    public IReadOnlyList<KitchenOrderItemPreview> Items { get; init; } = [];

    public int EstimatedPrepTimeMinutes { get; init; }
    public string Notes { get; init; } = string.Empty;
}

/// <summary>
/// Slim address shape carried on the bus — does NOT depend on Ordering's
/// <c>Ordering.Domain.ValueObjects.Address</c> so that downstream services
/// can reference <c>BuildingBlocks.Messaging</c> alone.
/// </summary>
public record OrderAddress(
    string Street,
    string City,
    string State,
    string ZipCode,
    string Country);

/// <summary>
/// Per-item projection published with <see cref="OrderCreatedIntegrationEvent"/>.
/// Deliberately narrower than <c>Ordering.Application.Dtos.OrderItemDto</c>: no
/// payment-adjacent fields, no kitchen-write columns (<c>PrepStartedAt</c>,
/// <c>PrepCompletedAt</c>) — those are owned by Kitchen and Ordering
/// respectively and arrive on the bus later as their own events.
/// </summary>
public record KitchenOrderItemPreview(
    Guid OrderItemId,
    Guid MenuItemId,
    string MenuItemName,
    int Quantity,
    decimal UnitPrice,
    IReadOnlyList<string> SelectedVariations,
    IReadOnlyList<string> Customizations,
    string? SpecialInstructions,
    int? SeatNumber);