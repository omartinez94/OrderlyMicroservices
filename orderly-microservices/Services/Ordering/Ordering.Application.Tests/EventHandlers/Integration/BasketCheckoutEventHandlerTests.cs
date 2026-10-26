using System.Security.Claims;
using BuildingBlocks.Multitenancy;
using Ordering.Application.Orders.Commands.CreateOrder;

namespace Ordering.Application.Tests.EventHandlers.Integration;

public sealed class BasketCheckoutEventHandlerTests
{
    [Fact]
    public async Task Consume_GeneratesOrderNumberAndSendsCreateOrderCommand_WhileAttachingTenant()
    {
        ISender sender = Substitute.For<ISender>();
        IOrderNumberGenerator generator = Substitute.For<IOrderNumberGenerator>();
        ICurrentRestaurantProvider tenantProvider = Substitute.For<ICurrentRestaurantProvider>();
        ILogger<BasketCheckoutEventHandler> logger = NullLogger<BasketCheckoutEventHandler>.Instance;

        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        LocalDate testDate = new(2026, 10, 23);
        OrderNumber expectedOrderNumber = OrderNumber.Create(OrderType.Delivery, testDate, 42);

        generator.GenerateAsync(restaurantId, OrderType.Delivery, Arg.Any<CancellationToken>())
            .Returns(expectedOrderNumber);

        ClaimsPrincipal? attachedPrincipal = null;
        IDisposable scope = Substitute.For<IDisposable>();
        tenantProvider.Attach(Arg.Do<ClaimsPrincipal>(p => attachedPrincipal = p))
            .Returns(scope);

        BasketCheckoutEventHandler handler = new(sender, generator, logger, tenantProvider);

        BasketCheckoutEvent evt = new()
        {
            UserId = userId,
            RestaurantId = restaurantId,
            FirstName = "John",
            LastName = "Doe",
            EmailAddress = "john@example.com",
            TotalAmount = 50m,
            AddressLine = "123 Main St",
            City = "Springfield",
            State = "IL",
            Country = "US",
            ZipCode = "12345",
            PaymentMethodSummary = new PaymentMethodSummary(PaymentMethod.Card, "Visa", "1234"),
            Items =
            [
                new BasketCheckoutItem
                {
                    MenuItemId = 1,
                    Name = "Burger",
                    Quantity = 2,
                    UnitPrice = 25m,
                    TotalPrice = 50m,
                }
            ]
        };

        ConsumeContext<BasketCheckoutEvent> context = Substitute.For<ConsumeContext<BasketCheckoutEvent>>();
        context.Message.Returns(evt);
        context.CancellationToken.Returns(CancellationToken.None);

        CreateOrderCommand? capturedCommand = null;
        await sender.Send(Arg.Do<CreateOrderCommand>(c => capturedCommand = c), Arg.Any<CancellationToken>());

        await handler.Consume(context);

        attachedPrincipal.Should().NotBeNull();
        attachedPrincipal!.FindFirstValue("restaurantId").Should().Be(restaurantId.ToString());
        scope.Received(1).Dispose();

        await generator.Received(1).GenerateAsync(restaurantId, OrderType.Delivery, Arg.Any<CancellationToken>());
        capturedCommand.Should().NotBeNull();
        capturedCommand!.Order.OrderNumber.Should().Be("DEL20261023-0042");
        capturedCommand.Order.RestaurantId.Should().Be(restaurantId);
        capturedCommand.Order.CustomerId.Should().Be(userId);
        capturedCommand.Order.OrderItems.Should().HaveCount(1);
        capturedCommand.Order.OrderItems[0].MenuItemName.Should().Be("Burger");
    }

    [Fact]
    public async Task Consume_PopulatesItemMetadata_FromBasketCheckoutEvent()
    {
        ISender sender = Substitute.For<ISender>();
        IOrderNumberGenerator generator = Substitute.For<IOrderNumberGenerator>();
        ILogger<BasketCheckoutEventHandler> logger = NullLogger<BasketCheckoutEventHandler>.Instance;

        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        LocalDate testDate = new(2026, 10, 23);
        OrderNumber expectedOrderNumber = OrderNumber.Create(OrderType.Delivery, testDate, 43);

        generator.GenerateAsync(restaurantId, OrderType.Delivery, Arg.Any<CancellationToken>())
            .Returns(expectedOrderNumber);

        BasketCheckoutEventHandler handler = new(sender, generator, logger, tenantProvider: null);

        BasketCheckoutEvent evt = new()
        {
            UserId = userId,
            RestaurantId = restaurantId,
            FirstName = "Jane",
            LastName = "Doe",
            EmailAddress = "jane@example.com",
            TotalAmount = 30m,
            AddressLine = "456 Elm St",
            City = "Springfield",
            State = "IL",
            Country = "US",
            ZipCode = "12345",
            PaymentMethodSummary = new PaymentMethodSummary(PaymentMethod.Card, "MasterCard", "5678"),
            Items =
            [
                new BasketCheckoutItem
                {
                    MenuItemId = 2,
                    Name = "Gourmet Burger",
                    Quantity = 1,
                    UnitPrice = 30m,
                    TotalPrice = 30m,
                    Customizations =
                    [
                        new BasketItemCustomizationDto { Ingredient = "Lettuce", Action = "Extra" },
                        new BasketItemCustomizationDto { Ingredient = "Pickles", Action = string.Empty }
                    ],
                    Variations =
                    [
                        new BasketItemVariationDto { Name = "Patty", Value = "Well Done", Price = 0m },
                        new BasketItemVariationDto { Name = "Cheese", Value = string.Empty, Price = 2m }
                    ]
                }
            ]
        };

        ConsumeContext<BasketCheckoutEvent> context = Substitute.For<ConsumeContext<BasketCheckoutEvent>>();
        context.Message.Returns(evt);
        context.CancellationToken.Returns(CancellationToken.None);

        CreateOrderCommand? capturedCommand = null;
        await sender.Send(Arg.Do<CreateOrderCommand>(c => capturedCommand = c), Arg.Any<CancellationToken>());

        await handler.Consume(context);

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Order.OrderItems.Should().HaveCount(1);
        OrderItemDto item = capturedCommand.Order.OrderItems[0];
        item.MenuItemName.Should().Be("Gourmet Burger");
        item.Customizations.Should().HaveCount(2);
        item.Customizations[0].Label.Should().Be("Lettuce");
        item.Customizations[0].Value.Should().Be("Extra");
        item.Customizations[1].Label.Should().Be("Pickles");
        item.Customizations[1].Value.Should().BeNull();
        item.SelectedVariations.Should().HaveCount(2);
        item.SelectedVariations[0].Name.Should().Be("Patty: Well Done");
        item.SelectedVariations[0].Price.Should().Be(0m);
        item.SelectedVariations[1].Name.Should().Be("Cheese");
        item.SelectedVariations[1].Price.Should().Be(2m);
    }
}
