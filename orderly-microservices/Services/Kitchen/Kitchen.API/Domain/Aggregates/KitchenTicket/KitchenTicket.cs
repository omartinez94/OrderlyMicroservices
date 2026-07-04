namespace Kitchen.API.Domain.Aggregates.KitchenTicket;

/// <summary>
/// The kitchen-side projection of an <c>Order</c>. One ticket per order,
/// 1:1 by id (see <see cref="Domain.ValueObjects.KitchenTicketId"/>).
///
/// Behaviour methods enforce the legal transition table declared on
/// <see cref="Domain.Enums.KitchenTicketStatus"/>; illegal transitions throw
/// <see cref="Domain.Exceptions.InvalidKitchenTicketStateTransitionException"/>.
/// Every state-changing method raises a domain event so application-level
/// handlers can broadcast over SignalR and Ordering can react to the
/// outbound integration events.
/// </summary>
public class KitchenTicket : Aggregate<KitchenTicketId>
{
    private readonly List<KitchenTicketItem> _items = [];

    // EF Core.
    private KitchenTicket() { }

    private KitchenTicket(
        KitchenTicketId id,
        Guid restaurantId,
        Guid customerId,
        string orderNumber,
        Instant receivedAt)
    {
        Id = id;
        RestaurantId = restaurantId;
        CustomerId = customerId;
        OrderNumber = orderNumber;
        ReceivedAt = receivedAt;
        Status = KitchenTicketStatus.New;
    }

    public Guid RestaurantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string OrderNumber { get; private set; } = default!;
    public KitchenTicketStatus Status { get; private set; }
    public Instant ReceivedAt { get; private set; }
    public Instant? StartedAt { get; private set; }
    public Instant? ReadyAt { get; private set; }
    public Instant? BumpedAt { get; private set; }
    public Guid? ConfirmedByUserId { get; private set; }
    public string? CancellationReason { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public Instant? CancelledAt { get; private set; }
    public string Notes { get; private set; } = string.Empty;

    public IReadOnlyCollection<KitchenTicketItem> Items => _items.AsReadOnly();

    /// <summary>
    /// Builds a new ticket from an inbound <c>OrderCreatedIntegrationEvent</c>.
    /// Called from <c>OrderCreatedIntegrationEventHandler</c>; never from user
    /// input directly.
    /// </summary>
    public static KitchenTicket CreateFromOrder(
        Guid orderId,
        Guid restaurantId,
        Guid customerId,
        string orderNumber,
        IReadOnlyList<Kitchen.API.Domain.Events.OrderItemSeed> itemSeeds,
        string notes,
        Instant receivedAt)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("orderId cannot be empty.", nameof(orderId));
        if (restaurantId == Guid.Empty)
            throw new ArgumentException("restaurantId cannot be empty.", nameof(restaurantId));
        if (customerId == Guid.Empty)
            throw new ArgumentException("customerId cannot be empty.", nameof(customerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        ArgumentNullException.ThrowIfNull(itemSeeds);

        var ticket = new KitchenTicket(
            KitchenTicketId.Of(orderId),
            restaurantId,
            customerId,
            orderNumber,
            receivedAt)
        {
            Notes = notes ?? string.Empty,
        };

        foreach (var seed in itemSeeds)
        {
            ticket._items.Add(new KitchenTicketItem(
                id: KitchenItemId.Of(seed.OrderItemId),
                orderItemId: seed.OrderItemId,
                menuItemId: seed.MenuItemId,
                menuItemName: seed.MenuItemName,
                quantity: seed.Quantity,
                unitPrice: seed.UnitPrice,
                selectedVariations: seed.SelectedVariations,
                customizations: seed.Customizations,
                specialInstructions: seed.SpecialInstructions,
                seatNumber: seed.SeatNumber));
        }

        return ticket;
    }

    // ----- behaviour -----

    /// <summary>
    /// <c>New -&gt; InProgress</c>. Records the staff user who accepted the
    /// ticket and the moment the first item entered preparation.
    /// </summary>
    public void Accept(Guid staffUserId, Instant now)
    {
        if (Status != KitchenTicketStatus.New)
            throw new InvalidKitchenTicketStateTransitionException(Status, nameof(Accept));

        if (staffUserId == Guid.Empty)
            throw new ArgumentException("staffUserId cannot be empty.", nameof(staffUserId));

        Status = KitchenTicketStatus.InProgress;
        ConfirmedByUserId = staffUserId;
        StartedAt = now;

        AddDomainEvent(new KitchenTicketAcceptedEvent(this, staffUserId, now));
    }

    /// <summary>
    /// Per-item start. The aggregate moves only the requested item; status
    /// stays <c>InProgress</c> until every item is <c>Ready</c>.
    /// </summary>
    public void StartItemPrep(KitchenItemId itemId, Instant now)
    {
        if (Status != KitchenTicketStatus.New && Status != KitchenTicketStatus.InProgress)
            throw new InvalidKitchenTicketStateTransitionException(Status, nameof(StartItemPrep));

        var item = FindItem(itemId);
        item.Start(now);

        if (StartedAt is null)
            StartedAt = now;

        AddDomainEvent(new KitchenTicketItemPrepStartedEvent(this, itemId, now));
    }

    /// <summary>
    /// Per-item ready. Does NOT move the aggregate to <c>Ready</c> by itself
    /// — the aggregate-level <see cref="MarkReady"/> is the only path that
    /// flips status once every item reports ready.
    /// </summary>
    public void MarkItemReady(KitchenItemId itemId, Instant now)
    {
        // Per-item ready is allowed while the ticket is still active (New or
        // InProgress). Once the ticket is Ready/Bumped/Cancelled, individual
        // items are frozen.
        if (Status != KitchenTicketStatus.New && Status != KitchenTicketStatus.InProgress)
            throw new InvalidKitchenTicketStateTransitionException(Status, nameof(MarkItemReady));

        var item = FindItem(itemId);
        item.MarkReady(now);

        AddDomainEvent(new KitchenTicketItemReadyEvent(this, itemId, now));
    }

    /// <summary>
    /// <c>InProgress -&gt; Ready</c>. Permitted only when every item is in
    /// <see cref="KitchenItemStatus.Ready"/>.
    /// </summary>
    public void MarkReady(Instant now)
    {
        if (Status != KitchenTicketStatus.InProgress)
            throw new InvalidKitchenTicketStateTransitionException(Status, nameof(MarkReady));

        if (_items.Any(i => i.Status != KitchenItemStatus.Ready))
            throw new KitchenDomainException(
                "Cannot mark ticket ready while any item is still preparing.",
                nameof(MarkReady));

        Status = KitchenTicketStatus.Ready;
        ReadyAt = now;

        AddDomainEvent(new KitchenTicketReadyEvent(this, now));
    }

    /// <summary>
    /// <c>Ready -&gt; Bumped</c>. The expo has acknowledged the ticket.
    /// </summary>
    public void Bump(Instant now)
    {
        if (Status != KitchenTicketStatus.Ready)
            throw new InvalidKitchenTicketStateTransitionException(Status, nameof(Bump));

        Status = KitchenTicketStatus.Bumped;
        BumpedAt = now;

        AddDomainEvent(new KitchenTicketBumpedEvent(this, now));
    }

    /// <summary>
    /// <c>Bumped -&gt; Ready</c>. Used when the chef pulls a ticket back
    /// after a premature bump.
    /// </summary>
    public void Recall(Instant now)
    {
        if (Status != KitchenTicketStatus.Bumped)
            throw new InvalidKitchenTicketStateTransitionException(Status, nameof(Recall));

        Status = KitchenTicketStatus.Ready;
        BumpedAt = null;

        AddDomainEvent(new KitchenTicketRecalledEvent(this, now));
    }

    /// <summary>
    /// Permitted from any non-terminal state. Records reason and the user
    /// who cancelled for audit.
    /// </summary>
    public void Cancel(string reason, Guid userId, Instant now)
    {
        if (Status == KitchenTicketStatus.Cancelled)
            throw new InvalidKitchenTicketStateTransitionException(Status, nameof(Cancel));

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (userId == Guid.Empty)
            throw new ArgumentException("userId cannot be empty.", nameof(userId));

        Status = KitchenTicketStatus.Cancelled;
        CancellationReason = reason;
        CancelledByUserId = userId;
        CancelledAt = now;

        AddDomainEvent(new KitchenTicketCancelledEvent(this, reason, userId, now));
    }

    private KitchenTicketItem FindItem(KitchenItemId itemId)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
            throw new KitchenDomainException(
                $"Item {itemId.Value} does not belong to ticket {Id.Value}.",
                nameof(itemId));
        return item;
    }
}