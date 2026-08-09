using Ordering.Application.Orders.Commands.UpdateOrder;

namespace Ordering.Application.Tests.Commands;

/// <summary>
/// Covers <see cref="UpdateOrderHandler"/>: looks the order up via
/// <c>FindAsync</c>, mutates addresses / payment / scalar fields, and
/// persists. Negative path: unknown id throws <see cref="OrderNotFoundException"/>.
/// </summary>
public sealed class UpdateOrderHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static AddressDto ValidAddressDto() =>
        new("123 Main St", "Springfield", "IL", "12345", "US");

    private static PaymentDto ValidPaymentDto() => new(PaymentMethod.Card, "Visa", "1111");

    private static OrderItemDto ValidItemDto(MenuItemId menuItemId) => new(
        Id: Guid.NewGuid(),
        OrderId: Guid.NewGuid(),
        MenuItemId: menuItemId.Value,
        MenuItemName: "Burger",
        MenuItemDescription: "A burger",
        MenuItemImageUrl: "https://example.test/burger.png",
        Quantity: 1,
        UnitPrice: 9.99m,
        BasePrice: 9.99m,
        TotalPrice: 9.99m,
        SeatNumber: 1,
        SpecialInstructions: string.Empty,
        Customizations: [],
        SelectedVariations: [],
        PrepStatus: PrepStatus.Pending,
        CreatedAt: SystemClock.Instance.GetCurrentInstant(),
        PrepStartedAt: null,
        PrepCompletedAt: null);

    private static Order SeedPendingOrder()
    {
        var order = Order.Create(
            NewOrderId(),
            NewCustomerId(),
            OrderNumber.Of("ORD-2026-0001"),
            Guid.NewGuid(),
            ValidAddress(),
            ValidAddress(),
            ValidPayment());
        order.Add(NewMenuItemId(), quantity: 1, price: 9.99m);
        order.ClearDomainEvents();
        return order;
    }

    private static OrderDto BuildUpdateDto(Guid orderId) => new(
        Id: orderId,
        CustomerId: NewCustomerId().Value,
        OrderNumber: "ORD-2026-0001",
        RestaurantId: Guid.NewGuid(),
        Currency: "USD",
        Subtotal: 12.50m,
        TaxRate: 0.08m,
        TaxAmount: 1m,
        DiscountAmount: 5m,
        DiscountCode: "SAVE5",
        TotalAmount: 8.50m,
        Status: OrderStatus.Pending,
        OrderType: OrderType.DineIn,
        BillingAddress: ValidAddressDto(),
        DeliveryAddress: ValidAddressDto(),
        DeliveryNotes: "ring bell",
        DeliveryStatus: null,
        DeliveryLatitude: null,
        DeliveryLongitude: null,
        Payment: ValidPaymentDto(),
        EstimatedPrepTimeMinutes: 20,
        ActualPrepTimeMinutes: 0,
        IsModified: true,
        RequiresAdminApproval: false,
        TableId: null,
        CreatedByUserId: null,
        ApprovedByAdminId: null,
        ConfirmedByUserId: null,
        CompletedByUserId: null,
        ApprovedAt: null,
        CancelledAt: null,
        CompletedAt: null,
        ConfirmedAt: null,
        DeliveredAt: null,
        PreparingStartedAt: null,
        ReadyAt: null,
        Notes: "updated notes",
        OrderItems: [ValidItemDto(NewMenuItemId())],
        Activities: []);

    [Fact]
    public async Task Handle_ExistingOrder_UpdatesAndSaves()
    {
        var order = SeedPendingOrder();
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders
            .FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(order));

        var dto = BuildUpdateDto(order.Id.Value);
        var handler = new UpdateOrderHandler(dbContext);

        var result = await handler.Handle(new UpdateOrderCommand(dto), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.DiscountAmount.Should().Be(5m);
        order.DiscountCode.Should().Be("SAVE5");
        order.Subtotal.Should().Be(12.50m);
        order.TotalAmount.Should().Be(8.50m);
        order.Notes.Should().Be("updated notes");
        order.IsModified.Should().BeTrue();
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownOrder_ThrowsNotFound()
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders
            .FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(null));

        var dto = BuildUpdateDto(Guid.NewGuid());
        var handler = new UpdateOrderHandler(dbContext);

        Func<Task> act = () => handler.Handle(
            new UpdateOrderCommand(dto),
            CancellationToken.None);

        await act.Should().ThrowAsync<OrderNotFoundException>();
    }
}
