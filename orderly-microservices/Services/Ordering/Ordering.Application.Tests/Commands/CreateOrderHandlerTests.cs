using BuildingBlocks.Messaging.Events;
using Ordering.Application.Orders.Commands.CreateOrder;

namespace Ordering.Application.Tests.Commands;

/// <summary>
/// Covers <see cref="CreateOrderHandler"/>: builds the <c>Order</c>
/// aggregate from the inbound <see cref="OrderDto"/>, generates
/// order number when not provided, appends one
/// <c>OrderCreated</c> activity, persists, and returns the new id.
/// </summary>
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

    private static OrderDto NewValidOrderDto(IEnumerable<OrderItemDto> items, string orderNumber = "ORD-2026-0001") => new(
        Id: Guid.NewGuid(),
        CustomerId: NewCustomerId().Value,
        OrderNumber: orderNumber,
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
        var orderNumberGenerator = Substitute.For<IOrderNumberGenerator>();
        Order? captured = null;
        dbContext.Orders.Add(Arg.Do<Order>(o => captured = o));

        var handler = new CreateOrderHandler(dbContext, orderNumberGenerator);
        var result = await handler.Handle(new CreateOrderCommand(dto), CancellationToken.None);

        result.Id.Should().NotBe(Guid.Empty);
        captured.Should().NotBeNull();
        captured!.Id.Value.Should().Be(result.Id);
        captured.OrderNumber.Value.Should().Be("ORD-2026-0001");
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await orderNumberGenerator.DidNotReceive().GenerateAsync(Arg.Any<Guid>(), Arg.Any<OrderType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithEmptyOrderNumber_GeneratesFromOrderNumberGenerator()
    {
        var dto = NewValidOrderDto(new[] { ValidItemDto(NewMenuItemId()) }, orderNumber: string.Empty);
        var dbContext = Substitute.For<IApplicationDbContext>();
        var orderNumberGenerator = Substitute.For<IOrderNumberGenerator>();
        var generatedNumber = OrderNumber.Create("DIN", new LocalDate(2026, 10, 23), 1);
        orderNumberGenerator.GenerateAsync(dto.RestaurantId, dto.OrderType, Arg.Any<CancellationToken>())
            .Returns(generatedNumber);

        Order? captured = null;
        dbContext.Orders.Add(Arg.Do<Order>(o => captured = o));

        var handler = new CreateOrderHandler(dbContext, orderNumberGenerator);
        var result = await handler.Handle(new CreateOrderCommand(dto), CancellationToken.None);

        result.Id.Should().NotBe(Guid.Empty);
        captured.Should().NotBeNull();
        captured!.OrderNumber.Value.Should().Be("DIN20261023-0001");
        await orderNumberGenerator.Received(1).GenerateAsync(dto.RestaurantId, dto.OrderType, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithDefaultOrderNumber_GeneratesFromOrderNumberGenerator()
    {
        var dto = NewValidOrderDto(new[] { ValidItemDto(NewMenuItemId()) }, orderNumber: "default");
        var dbContext = Substitute.For<IApplicationDbContext>();
        var orderNumberGenerator = Substitute.For<IOrderNumberGenerator>();
        var generatedNumber = OrderNumber.Create(OrderType.DineIn, new LocalDate(2026, 10, 23), 5);
        orderNumberGenerator.GenerateAsync(dto.RestaurantId, dto.OrderType, Arg.Any<CancellationToken>())
            .Returns(generatedNumber);

        Order? captured = null;
        dbContext.Orders.Add(Arg.Do<Order>(o => captured = o));

        var handler = new CreateOrderHandler(dbContext, orderNumberGenerator);
        var result = await handler.Handle(new CreateOrderCommand(dto), CancellationToken.None);

        result.Id.Should().NotBe(Guid.Empty);
        captured.Should().NotBeNull();
        captured!.OrderNumber.Value.Should().Be("DIN20261023-0005");
        await orderNumberGenerator.Received(1).GenerateAsync(dto.RestaurantId, dto.OrderType, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_RaisesOrderCreatedDomainEvent()
    {
        var dto = NewValidOrderDto(new[] { ValidItemDto(NewMenuItemId()) });
        var dbContext = Substitute.For<IApplicationDbContext>();
        var orderNumberGenerator = Substitute.For<IOrderNumberGenerator>();
        Order? captured = null;
        dbContext.Orders.Add(Arg.Do<Order>(o => captured = o));

        var handler = new CreateOrderHandler(dbContext, orderNumberGenerator);
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
        var orderNumberGenerator = Substitute.For<IOrderNumberGenerator>();
        Order? captured = null;
        dbContext.Orders.Add(Arg.Do<Order>(o => captured = o));

        var handler = new CreateOrderHandler(dbContext, orderNumberGenerator);
        await handler.Handle(new CreateOrderCommand(dto), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.OrderItems.Should().HaveCount(3);
        captured.OrderItems.Select(i => i.MenuItemId).Should().BeEquivalentTo(
            items.Select(i => MenuItemId.Of(i.MenuItemId)));
    }

    [Fact]
    public async Task Handle_ValidCommand_PreservesOrderItemMetadata()
    {
        KitchenOrderItemCustomization customization = new("Extra sauce", "Hot", 0.50m);
        KitchenOrderItemVariation variation = new("Size: Medium", 1.50m);
        OrderItemDto itemDto = new(
            Id: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid(),
            MenuItemName: "Deluxe Pizza",
            MenuItemDescription: "Delicious",
            MenuItemImageUrl: "https://example.test/pizza.png",
            Quantity: 2,
            UnitPrice: 15.00m,
            BasePrice: 15.00m,
            TotalPrice: 30.00m,
            SeatNumber: 3,
            SpecialInstructions: "Well done crust",
            Customizations: [customization],
            SelectedVariations: [variation],
            PrepStatus: PrepStatus.Pending,
            CreatedAt: SystemClock.Instance.GetCurrentInstant(),
            PrepStartedAt: null,
            PrepCompletedAt: null);

        OrderDto dto = NewValidOrderDto([itemDto]);
        IApplicationDbContext dbContext = Substitute.For<IApplicationDbContext>();
        IOrderNumberGenerator orderNumberGenerator = Substitute.For<IOrderNumberGenerator>();
        Order? captured = null;
        dbContext.Orders.Add(Arg.Do<Order>(o => captured = o));

        CreateOrderHandler handler = new(dbContext, orderNumberGenerator);
        await handler.Handle(new CreateOrderCommand(dto), CancellationToken.None);

        captured.Should().NotBeNull();
        OrderItem item = captured!.OrderItems.Single();
        item.MenuItemName.Should().Be("Deluxe Pizza");
        item.SpecialInstructions.Should().Be("Well done crust");
        item.SeatNumber.Should().Be(3);
        item.UnitPrice.Should().Be(15.00m);
        item.TotalPrice.Should().Be(30.00m);
        item.Customizations.Should().ContainSingle().Which.Should().BeEquivalentTo(customization);
        item.SelectedVariations.Should().ContainSingle().Which.Should().BeEquivalentTo(variation);
    }
}
