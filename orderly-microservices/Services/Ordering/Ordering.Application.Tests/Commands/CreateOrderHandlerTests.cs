using BuildingBlocks.Messaging.Events;
using Ordering.Application.Orders.Commands.CreateOrder;

namespace Ordering.Application.Tests.Commands;

/// <summary>
/// Covers <see cref="CreateOrderHandler"/>: builds the <c>Order</c>
/// aggregate from the inbound <see cref="OrderDto"/>, appends one
/// <c>OrderCreated</c> activity, persists, and returns the new id.
/// </summary>
/// <remarks>
/// <para>
/// Per <c>.agents/plan/testing/E2E_TEST_COVERAGE_PLAN.md</c> §6.3 the
/// third assertion is "persists all order items" — the handler maps
/// only <c>MenuItemId</c> / <c>Quantity</c> / <c>UnitPrice</c> from each
/// <c>OrderItemDto</c> (display fields like <c>MenuItemName</c> are
/// discarded by <c>Order.Add</c>), so the assertion is on item count,
/// not on display fields.
/// </para>
/// </remarks>
public sealed class CreateOrderHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());
    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");
    private static PaymentDto ValidPayment() => new(PaymentMethod.Card, "Visa", "1111");
    private static AddressDto ValidAddressDto() =>
        new("123 Main St", "Springfield", "IL", "12345", "US");

    private static OrderItemDto ValidItemDto(MenuItemId menuItemId) => new(
        Id: Guid.NewGuid(),
        OrderId: Guid.NewGuid(),
        MenuItemId: menuItemId.Value,
        MenuItemName: "Burger",
        MenuItemDescription: "A burger",
        MenuItemImageUrl: "https://example.test/burger.png",
        Quantity: 2,
        UnitPrice: 9.99m,
        BasePrice: 9.99m,
        TotalPrice: 19.98m,
        SeatNumber: 1,
        SpecialInstructions: "no onions",
        Customizations: [],
        SelectedVariations: [],
        PrepStatus: PrepStatus.Pending,
        CreatedAt: SystemClock.Instance.GetCurrentInstant(),
        PrepStartedAt: null,
        PrepCompletedAt: null);

    private static OrderDto NewValidOrderDto(IEnumerable<OrderItemDto> items) => new(
        Id: Guid.NewGuid(),
        CustomerId: NewCustomerId().Value,
        OrderNumber: "ORD-2026-0001",
        RestaurantId: Guid.NewGuid(),
        Currency: "USD",
        Subtotal: 100m,
        TaxRate: 0.08m,
        TaxAmount: 8m,
        DiscountAmount: 0m,
        DiscountCode: string.Empty,
        TotalAmount: 108m,
        Status: OrderStatus.Pending,
        OrderType: OrderType.DineIn,
        BillingAddress: ValidAddressDto(),
        DeliveryAddress: ValidAddressDto(),
        DeliveryNotes: "leave at door",
        DeliveryStatus: null,
        DeliveryLatitude: null,
        DeliveryLongitude: null,
        Payment: ValidPayment(),
        EstimatedPrepTimeMinutes: 15,
        ActualPrepTimeMinutes: 0,
        IsModified: false,
        RequiresAdminApproval: false,
        TableId: null,
        CreatedByUserId: Guid.NewGuid(),
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
        Notes: "extra napkins",
        OrderItems: [.. items],
        Activities: []);

    [Fact]
    public async Task Handle_ValidCommand_CreatesOrderAndReturnsId()
    {
        var dto = NewValidOrderDto(new[] { ValidItemDto(NewMenuItemId()) });
        var dbContext = Substitute.For<IApplicationDbContext>();
        Order? captured = null;
        dbContext.Orders.Add(Arg.Do<Order>(o => captured = o));

        var handler = new CreateOrderHandler(dbContext);
        var result = await handler.Handle(new CreateOrderCommand(dto), CancellationToken.None);

        result.Id.Should().NotBe(Guid.Empty);
        captured.Should().NotBeNull();
        captured!.Id.Value.Should().Be(result.Id);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_RaisesOrderCreatedDomainEvent()
    {
        var dto = NewValidOrderDto(new[] { ValidItemDto(NewMenuItemId()) });
        var dbContext = Substitute.For<IApplicationDbContext>();
        Order? captured = null;
        dbContext.Orders.Add(Arg.Do<Order>(o => captured = o));

        var handler = new CreateOrderHandler(dbContext);
        await handler.Handle(new CreateOrderCommand(dto), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.DomainEvents.Should().ContainSingle(e => e is OrderCreatedEvent);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsAllOrderItems()
    {
        var items = new[]
        {
            ValidItemDto(NewMenuItemId()),
            ValidItemDto(NewMenuItemId()),
            ValidItemDto(NewMenuItemId()),
        };
        var dto = NewValidOrderDto(items);
        var dbContext = Substitute.For<IApplicationDbContext>();
        Order? captured = null;
        dbContext.Orders.Add(Arg.Do<Order>(o => captured = o));

        var handler = new CreateOrderHandler(dbContext);
        await handler.Handle(new CreateOrderCommand(dto), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.OrderItems.Should().HaveCount(3);
        captured.OrderItems.Select(i => i.MenuItemId).Should().BeEquivalentTo(
            items.Select(i => MenuItemId.Of(i.MenuItemId)));
    }
}
