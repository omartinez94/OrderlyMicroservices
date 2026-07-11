using BuildingBlocks.Messaging.Events;

namespace Ordering.Domain.Models;

public class OrderItem : Abstractions::Entity<OrderItemId>
{
    internal OrderItem(OrderId orderId, MenuItemId menuItemId, int quantity, decimal unitPrice)
    {
        Id = OrderItemId.Of(Guid.NewGuid());
        OrderId = orderId;
        MenuItemId = menuItemId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public decimal BasePrice { get; set; }
    public Instant CreatedAt { get; set; }
    /// <summary>
    /// Typed customizations carried on this item. The aggregate is the
    /// single source of truth for the on-disk jsonb column; the wire format
    /// (and the kitchen integration event) uses the same
    /// <see cref="KitchenOrderItemCustomization"/> record so no string
    /// round-trip is needed at the boundary.
    /// </summary>
    public IReadOnlyList<KitchenOrderItemCustomization> Customizations { get; set; }
        = Array.Empty<KitchenOrderItemCustomization>();
    public string MenuItemDescription { get; set; } = string.Empty;
    public string MenuItemImageUrl { get; set; } = string.Empty;
    public string MenuItemName { get; set; } = string.Empty;
    public OrderId OrderId { get; set; } = default!;
    public MenuItemId MenuItemId { get; set; } = default!;
    /// <summary>Preparation state: pending, preparing, ready</summary>
    public PrepStatus PrepStatus { get; set; } = PrepStatus.Pending;
    public int Quantity { get; set; }
    /// <summary>Used for bill splitting by seat</summary>
    public int SeatNumber { get; set; }
    /// <summary>
    /// Typed variations carried on this item. Mirrors
    /// <see cref="Customizations"/>: aggregate owns the typed array, EF
    /// Core serialises it to the existing <c>nvarchar(max)</c> jsonb
    /// column.
    /// </summary>
    public IReadOnlyList<KitchenOrderItemVariation> SelectedVariations { get; set; }
        = Array.Empty<KitchenOrderItemVariation>();
    public string SpecialInstructions { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public decimal UnitPrice { get; set; }
    public Instant? PrepCompletedAt { get; set; }
    public Instant? PrepStartedAt { get; set; }

    /// <summary>
    /// <c>Pending -&gt; Preparing</c>. Mutates the per-item prep state.
    /// The parent <see cref="Order"/>'s status is unaffected — that's the
    /// kitchen display's responsibility to track.
    /// </summary>
    public void MarkItemPreparing(Instant now)
    {
        if (PrepStatus != PrepStatus.Pending)
            throw new InvalidOrderItemStateTransitionException(PrepStatus, nameof(MarkItemPreparing));

        PrepStatus = PrepStatus.Preparing;
        PrepStartedAt = now;
    }

    /// <summary>
    /// <c>Preparing -&gt; Ready</c>. Idempotent: calling on an already-Ready
    /// item throws so the API surfaces a 409 instead of silently rewriting
    /// <see cref="PrepCompletedAt"/>.
    /// </summary>
    public void MarkItemReady(Instant now)
    {
        if (PrepStatus != PrepStatus.Preparing)
            throw new InvalidOrderItemStateTransitionException(PrepStatus, nameof(MarkItemReady));

        PrepStatus = PrepStatus.Ready;
        PrepCompletedAt = now;
    }
}
