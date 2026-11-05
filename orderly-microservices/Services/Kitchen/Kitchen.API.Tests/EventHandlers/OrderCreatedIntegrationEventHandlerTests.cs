using Kitchen.API.Application.Dtos;
using Kitchen.API.Application.Extensions;

namespace Kitchen.API.Tests.EventHandlers;

public sealed class OrderCreatedIntegrationEventHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 4, 18, 0);

    [Fact]
    public async Task Consume_PersistsTicketItemsWithCourseNumber_AndBroadcastsSignalR()
    {
        IKitchenTicketRepository repository = Substitute.For<IKitchenTicketRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        IHubContext<KitchenHub, IKitchenHubClient> hubContext = Substitute.For<IHubContext<KitchenHub, IKitchenHubClient>>();
        IHubClients<IKitchenHubClient> hubClients = Substitute.For<IHubClients<IKitchenHubClient>>();
        IKitchenHubClient groupProxy = Substitute.For<IKitchenHubClient>();

        hubContext.Clients.Returns(hubClients);
        hubClients.Group(Arg.Any<string>()).Returns(groupProxy);

        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((KitchenTicket?)null);

        OrderCreatedIntegrationEventHandler handler = new(
            repository,
            unitOfWork,
            hubContext,
            NullLogger<OrderCreatedIntegrationEventHandler>.Instance);

        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid customerId = Guid.NewGuid();
        Guid starterItemId = Guid.NewGuid();
        Guid mainItemId = Guid.NewGuid();

        OrderCreatedIntegrationEvent evt = new OrderCreatedIntegrationEvent
        {
            OrderId = orderId,
            OrderNumber = "ORD-POS-1001",
            RestaurantId = restaurantId,
            CustomerId = customerId,
            OccurredOn = Now,
            Subtotal = 45m,
            TotalAmount = 45m,
            TaxAmount = 0m,
            Currency = "USD",
            Items =
            [
                new KitchenOrderItemPreview(
                    OrderItemId: starterItemId,
                    MenuItemId: Guid.NewGuid(),
                    MenuItemName: "Soup of the day",
                    Quantity: 1,
                    UnitPrice: 10m,
                    SelectedVariations: [],
                    Customizations: [],
                    SpecialInstructions: "Extra hot",
                    SeatNumber: 1,
                    CourseNumber: 1),
                new KitchenOrderItemPreview(
                    OrderItemId: mainItemId,
                    MenuItemId: Guid.NewGuid(),
                    MenuItemName: "Ribeye Steak",
                    Quantity: 1,
                    UnitPrice: 35m,
                    SelectedVariations: [],
                    Customizations: [],
                    SpecialInstructions: "Medium rare",
                    SeatNumber: 1,
                    CourseNumber: 2)
            ]
        };

        ConsumeContext<OrderCreatedIntegrationEvent> context = Substitute.For<ConsumeContext<OrderCreatedIntegrationEvent>>();
        context.Message.Returns(evt);
        context.CancellationToken.Returns(CancellationToken.None);

        await handler.Consume(context);

        // Verify repository received persisted ticket with CourseNumber preserved
        await repository.Received(1).AddAsync(
            Arg.Is<KitchenTicket>(t =>
                t.Id.Value == orderId &&
                t.RestaurantId == restaurantId &&
                t.OrderNumber == "ORD-POS-1001" &&
                t.Items.Count == 2 &&
                t.Items.First(i => i.OrderItemId == starterItemId).CourseNumber == 1 &&
                t.Items.First(i => i.OrderItemId == starterItemId).SeatNumber == 1 &&
                t.Items.First(i => i.OrderItemId == mainItemId).CourseNumber == 2 &&
                t.Items.First(i => i.OrderItemId == mainItemId).SeatNumber == 1),
            Arg.Any<CancellationToken>());

        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Verify SignalR broadcast includes the mapped CourseNumber on KitchenTicketDto items
        hubClients.Received(1).Group($"restaurant:{restaurantId}");
        await groupProxy.Received(1).OrderReceived(Arg.Is<KitchenTicketDto>(dto =>
            dto.Id == orderId &&
            dto.RestaurantId == restaurantId &&
            dto.OrderNumber == "ORD-POS-1001" &&
            dto.Items.Count == 2 &&
            dto.Items.First(i => i.OrderItemId == starterItemId).CourseNumber == 1 &&
            dto.Items.First(i => i.OrderItemId == starterItemId).SeatNumber == 1 &&
            dto.Items.First(i => i.OrderItemId == mainItemId).CourseNumber == 2 &&
            dto.Items.First(i => i.OrderItemId == mainItemId).SeatNumber == 1));
    }

    [Fact]
    public async Task Consume_WhenTicketAlreadyExists_SkipsPersistenceAndBroadcast()
    {
        IKitchenTicketRepository repository = Substitute.For<IKitchenTicketRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        IHubContext<KitchenHub, IKitchenHubClient> hubContext = Substitute.For<IHubContext<KitchenHub, IKitchenHubClient>>();
        IHubClients<IKitchenHubClient> hubClients = Substitute.For<IHubClients<IKitchenHubClient>>();

        Guid orderId = Guid.NewGuid();
        KitchenTicket existingTicket = KitchenTicket.CreateFromOrder(
            orderId: orderId,
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: "ORD-EXISTING-01",
            itemSeeds: [],
            notes: string.Empty,
            receivedAt: Now);

        repository.GetByIdAsync(orderId, Arg.Any<CancellationToken>()).Returns(existingTicket);

        OrderCreatedIntegrationEventHandler handler = new(
            repository,
            unitOfWork,
            hubContext,
            NullLogger<OrderCreatedIntegrationEventHandler>.Instance);

        OrderCreatedIntegrationEvent evt = new OrderCreatedIntegrationEvent
        {
            OrderId = orderId,
            OrderNumber = "ORD-EXISTING-01",
            RestaurantId = existingTicket.RestaurantId,
            CustomerId = existingTicket.CustomerId,
            OccurredOn = Now,
            Items = []
        };

        ConsumeContext<OrderCreatedIntegrationEvent> context = Substitute.For<ConsumeContext<OrderCreatedIntegrationEvent>>();
        context.Message.Returns(evt);
        context.CancellationToken.Returns(CancellationToken.None);

        await handler.Consume(context);

        await repository.DidNotReceive().AddAsync(Arg.Any<KitchenTicket>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _ = hubClients.DidNotReceive().Group(Arg.Any<string>());
    }
}
