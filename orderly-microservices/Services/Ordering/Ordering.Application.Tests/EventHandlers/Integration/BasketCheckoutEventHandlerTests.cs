using Ordering.Application.Orders.Commands.CreateOrder;

namespace Ordering.Application.Tests.EventHandlers.Integration;

public sealed class BasketCheckoutEventHandlerTests
{
    [Fact]
    public async Task Consume_GeneratesOrderNumberAndSendsCreateOrderCommand()
    {
        ISender sender = Substitute.For<ISender>();
        IOrderNumberGenerator generator = Substitute.For<IOrderNumberGenerator>();
        ILogger<BasketCheckoutEventHandler> logger = NullLogger<BasketCheckoutEventHandler>.Instance;

        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        LocalDate testDate = new(2026, 10, 23);
        OrderNumber expectedOrderNumber = OrderNumber.Create(OrderType.Delivery, testDate, 42);

        generator.GenerateAsync(restaurantId, OrderType.Delivery, Arg.Any<CancellationToken>())
            .Returns(expectedOrderNumber);

        BasketCheckoutEventHandler handler = new(sender, generator, logger);

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

        await generator.Received(1).GenerateAsync(restaurantId, OrderType.Delivery, Arg.Any<CancellationToken>());
        capturedCommand.Should().NotBeNull();
        capturedCommand!.Order.OrderNumber.Should().Be("DEL20261023-0042");
        capturedCommand.Order.RestaurantId.Should().Be(restaurantId);
        capturedCommand.Order.CustomerId.Should().Be(userId);
    }
}
