namespace Kitchen.API.Domain.Aggregates.KitchenTicket;

/// <summary>
/// Per-line-item projection of an <c>OrderItem</c> as the kitchen sees it.
/// Mirrors the kitchen-relevant fields from <c>OrderItem</c> and adds the
/// local prep state. The aggregate owns its lifecycle via the parent
/// <see cref="KitchenTicket"/> — direct mutation of <c>Status</c> is intentionally
/// not exposed; only the parent's transition methods move items forward.
/// </summary>
public class KitchenTicketItem : Entity<KitchenItemId>
{
    internal KitchenTicketItem(
        KitchenItemId id,
        Guid orderItemId,
        Guid menuItemId,
        string menuItemName,
        int quantity,
        decimal unitPrice,
        IReadOnlyList<string> selectedVariations,
        IReadOnlyList<string> customizations,
        string? specialInstructions,
        int? seatNumber)
    {
        Id = id;
        OrderItemId = orderItemId;
        MenuItemId = menuItemId;
        MenuItemName = menuItemName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        SelectedVariations = selectedVariations;
        Customizations = customizations;
        SpecialInstructions = specialInstructions;
        SeatNumber = seatNumber;
        Status = KitchenItemStatus.Pending;
    }

    public Guid OrderItemId { get; private set; }
    public Guid MenuItemId { get; private set; }
    public string MenuItemName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public IReadOnlyList<string> SelectedVariations { get; private set; } = [];
    public IReadOnlyList<string> Customizations { get; private set; } = [];
    public string? SpecialInstructions { get; private set; }
    public int? SeatNumber { get; private set; }
    public KitchenItemStatus Status { get; private set; }
    public Instant? StartedAt { get; private set; }
    public Instant? ReadyAt { get; private set; }
    public Guid? StationId { get; private set; }

    internal void Start(Instant now)
    {
        if (Status != KitchenItemStatus.Pending)
        {
            throw new InvalidKitchenItemStateTransitionException(Status, nameof(Start));
        }

        Status = KitchenItemStatus.Preparing;
        StartedAt = now;
    }

    internal void MarkReady(Instant now)
    {
        if (Status != KitchenItemStatus.Preparing)
        {
            throw new InvalidKitchenItemStateTransitionException(Status, nameof(MarkReady));
        }

        Status = KitchenItemStatus.Ready;
        ReadyAt = now;
    }

    internal void AssignStation(Guid stationId)
    {
        StationId = stationId;
    }
}