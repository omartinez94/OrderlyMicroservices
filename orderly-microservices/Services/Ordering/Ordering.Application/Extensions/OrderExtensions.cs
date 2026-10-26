namespace Ordering.Application.Extensions;

public static class OrderExtensions
{
    public static IEnumerable<OrderDto> ToOrderDtoList(this IEnumerable<Order> orders)
    {
        return orders.Select(o => o.ToOrderDto());
    }

    public static OrderDto ToOrderDto(this Order order)
    {
        return new OrderDto(
            Id: order.Id.Value,
            CustomerId: order.CustomerId.Value,
            OrderNumber: order.OrderNumber.Value,
            RestaurantId: order.RestaurantId,
            Currency: order.Currency,
            Subtotal: order.Subtotal,
            TaxRate: order.TaxRate,
            TaxAmount: order.TaxAmount,
            DiscountAmount: order.DiscountAmount,
            DiscountCode: order.DiscountCode,
            TotalAmount: order.TotalAmount,
            Status: order.Status,
            OrderType: order.OrderType,
            BillingAddress: new AddressDto(order.BillingAddress.Street, order.BillingAddress.City, order.BillingAddress.State, order.BillingAddress.ZipCode, order.BillingAddress.Country),
            DeliveryAddress: new AddressDto(order.DeliveryAddress.Street, order.DeliveryAddress.City, order.DeliveryAddress.State, order.DeliveryAddress.ZipCode, order.DeliveryAddress.Country),
            DeliveryNotes: order.DeliveryNotes,
            DeliveryStatus: order.DeliveryStatus,
            DeliveryLatitude: order.DeliveryLatitude,
            DeliveryLongitude: order.DeliveryLongitude,
            Payment: new PaymentDto(order.Payment.Method, order.Payment.Brand, order.Payment.LastFour),
            EstimatedPrepTimeMinutes: order.EstimatedPrepTimeMinutes,
            ActualPrepTimeMinutes: order.ActualPrepTimeMinutes,
            IsModified: order.IsModified,
            RequiresAdminApproval: order.RequiresAdminApproval,
            TableId: order.TableId,
            CreatedByUserId: order.CreatedByUserId,
            ApprovedByAdminId: order.ApprovedByAdminId,
            ConfirmedByUserId: order.ConfirmedByUserId,
            CompletedByUserId: order.CompletedByUserId,
            ApprovedAt: order.ApprovedAt,
            CancelledAt: order.CancelledAt,
            CompletedAt: order.CompletedAt,
            ConfirmedAt: order.ConfirmedAt,
            DeliveredAt: order.DeliveredAt,
            PreparingStartedAt: order.PreparingStartedAt,
            ReadyAt: order.ReadyAt,
            Notes: order.Notes,
            OrderItems: [.. order.OrderItems.Select(oi => new OrderItemDto(
                Id: oi.Id.Value,
                OrderId: oi.OrderId.Value,
                MenuItemId: oi.MenuItemId.Value,
                MenuItemName: oi.MenuItemName,
                MenuItemDescription: oi.MenuItemDescription,
                MenuItemImageUrl: oi.MenuItemImageUrl,
                Quantity: oi.Quantity,
                UnitPrice: oi.UnitPrice,
                BasePrice: oi.BasePrice,
                TotalPrice: oi.TotalPrice,
                SeatNumber: oi.SeatNumber,
                SpecialInstructions: oi.SpecialInstructions,
                Customizations: oi.Customizations,
                SelectedVariations: oi.SelectedVariations,
                PrepStatus: oi.PrepStatus,
                CreatedAt: oi.CreatedAt,
                PrepStartedAt: oi.PrepStartedAt,
                PrepCompletedAt: oi.PrepCompletedAt
            ))],
            Activities: [.. order.Activities
                .OrderBy(a => a.OccurredAt)
                .ThenBy(a => a.Id.Value)
                .Select(a => new OrderActivityDto(
                    Id: a.Id.Value,
                    ActivityType: a.ActivityType,
                    ActorUserId: a.ActorUserId,
                    OccurredAt: a.OccurredAt,
                    CorrelationId: a.CorrelationId,
                    Notes: a.Notes,
                    Metadata: a.Metadata))],
            ShortId: order.ShortId
        );
    }

    public static OrderSummaryDto ToOrderSummaryDto(this Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        IReadOnlyCollection<OrderItem> items = order.OrderItems;
        int itemCount = items.Sum(i => i.Quantity);
        int itemsReady = items.Count(i => i.PrepStatus == PrepStatus.Ready);
        int itemsPreparing = items.Count(i => i.PrepStatus == PrepStatus.Preparing);
        int itemsPending = items.Count(i => i.PrepStatus == PrepStatus.Pending);
        int itemsTotal = items.Count;
        bool hasSpecialInstructions = items.Any(i => !string.IsNullOrWhiteSpace(i.SpecialInstructions));
        bool hasNotes = !string.IsNullOrWhiteSpace(order.Notes);

        IReadOnlyList<OrderSummaryItemPreviewDto> preview =
        [
            .. items
                .Take(3)
                .Select(i => new OrderSummaryItemPreviewDto(
                    Name: i.MenuItemName,
                    Quantity: i.Quantity,
                    PrepStatus: i.PrepStatus))
        ];

        string? deliveryStreet = order.OrderType == OrderType.Delivery
            ? order.DeliveryAddress.Street.NullIfEmpty()
            : null;

        return new OrderSummaryDto(
            Id: order.Id.Value,
            ShortId: order.ShortId,
            OrderNumber: order.OrderNumber.Value,
            RestaurantId: order.RestaurantId,
            CustomerId: order.CustomerId.Value,
            Status: order.Status,
            OrderType: order.OrderType,
            DeliveryStatus: order.DeliveryStatus,
            CancellationReason: order.CancellationReason.NullIfEmpty(),
            TableId: order.TableId,
            DeliveryStreet: deliveryStreet,
            CreatedAt: order.CreatedAt,
            ConfirmedAt: order.ConfirmedAt,
            PreparingStartedAt: order.PreparingStartedAt,
            ReadyAt: order.ReadyAt,
            DeliveredAt: order.DeliveredAt,
            CompletedAt: order.CompletedAt,
            CancelledAt: order.CancelledAt,
            LastModifiedAt: order.LastModified,
            StatusChangedAt: order.ResolveStatusChangedAt(),
            EstimatedPrepTimeMinutes: order.EstimatedPrepTimeMinutes,
            ActualPrepTimeMinutes: order.ActualPrepTimeMinutes,
            Version: order.ResolveVersion(),
            Currency: order.Currency,
            Subtotal: order.Subtotal,
            TaxAmount: order.TaxAmount,
            DiscountAmount: order.DiscountAmount,
            TotalAmount: order.TotalAmount,
            PaymentMethod: order.Payment.Method,
            PaymentBrand: order.Payment.Brand,
            PaymentLastFour: order.Payment.LastFour,
            IsModified: order.IsModified,
            RequiresAdminApproval: order.RequiresAdminApproval,
            HasNotes: hasNotes,
            HasSpecialInstructions: hasSpecialInstructions,
            Notes: order.Notes.NullIfEmpty(),
            ItemCount: itemCount,
            ItemsReady: itemsReady,
            ItemsPreparing: itemsPreparing,
            ItemsPending: itemsPending,
            ItemsTotal: itemsTotal,
            ItemsPreview: preview,
            CreatedByUserId: order.CreatedByUserId,
            ConfirmedByUserId: order.ConfirmedByUserId);
    }

    public static Instant? ResolveStatusChangedAt(this Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        Instant? specific = ResolveSpecificStatusTimestamp(order);
        return order.StatusChangedAt ?? specific ?? order.LastModified ?? order.CreatedAt;
    }

    private static Instant? ResolveSpecificStatusTimestamp(Order order) =>
        order.Status switch
        {
            OrderStatus.Confirmed => order.ConfirmedAt,
            OrderStatus.Preparing => order.PreparingStartedAt ?? order.ConfirmedAt,
            OrderStatus.Ready => order.ReadyAt ?? order.PreparingStartedAt,
            OrderStatus.Delivered => order.DeliveredAt ?? order.ReadyAt,
            OrderStatus.Completed => order.CompletedAt ?? order.DeliveredAt,
            OrderStatus.Cancelled => order.CancelledAt,
            _ => null,
        };

    public static Instant? ResolveVersion(this Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return order.LastModified ?? order.CreatedAt ?? order.ConfirmedAt;
    }

    /// <summary>
    /// Maps the aggregate to the bus-safe <see cref="OrderCreatedIntegrationEvent"/>.
    /// Carries NO payment data — that field on <c>Order</c> is intentionally
    /// dropped here. See KITCHEN_INTEGRATION_PLAN.md Phase 1.
    /// </summary>
    public static OrderCreatedIntegrationEvent ToOrderCreatedIntegrationEvent(this Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderCreatedIntegrationEvent
        {
            OrderId = order.Id.Value,
            OrderNumber = order.OrderNumber.Value,
            RestaurantId = order.RestaurantId,
            TableId = order.TableId,
            OrderType = (int)order.OrderType,
            CustomerId = order.CustomerId.Value,
            Subtotal = order.Subtotal,
            TotalAmount = order.TotalAmount,
            TaxAmount = order.TaxAmount,
            DiscountAmount = order.DiscountAmount,
            Currency = order.Currency,
            DiscountCode = order.DiscountCode.NullIfEmpty(),
            BillingAddress = MapAddress(order.BillingAddress),
            DeliveryAddress = order.OrderType == OrderType.Delivery
                ? MapAddress(order.DeliveryAddress)
                : null,
            Items = order.OrderItems.Select(MapItem).ToList(),
            EstimatedPrepTimeMinutes = order.EstimatedPrepTimeMinutes,
            Notes = order.Notes ?? string.Empty,
        };
    }

    private static OrderAddress MapAddress(Address address) =>
        new(address.Street, address.City, address.State, address.ZipCode, address.Country);

    private static KitchenOrderItemPreview MapItem(OrderItem oi) =>
        new(
            OrderItemId: oi.Id.Value,
            MenuItemId: oi.MenuItemId.Value,
            MenuItemName: oi.MenuItemName,
            Quantity: oi.Quantity,
            UnitPrice: oi.UnitPrice,
            SelectedVariations: oi.SelectedVariations,
            Customizations: oi.Customizations,
            SpecialInstructions: oi.SpecialInstructions.NullIfEmpty(),
            SeatNumber: oi.SeatNumber > 0 ? oi.SeatNumber : null);

    /// <summary>
    /// Coerces <c>null</c> or empty-string to <c>null</c>; otherwise returns
    /// the original string. Used by the wire DTO projections so an unset
    /// optional column doesn't ship as <c>""</c> on the wire. <c>""</c> and
    /// whitespace-only are treated the same today — switch to
    /// <see cref="string.IsNullOrWhiteSpace"/> deliberately if downstream
    /// consumers need to distinguish.
    /// </summary>
    private static string? NullIfEmpty(this string? s) =>
        string.IsNullOrEmpty(s) ? null : s;
}
