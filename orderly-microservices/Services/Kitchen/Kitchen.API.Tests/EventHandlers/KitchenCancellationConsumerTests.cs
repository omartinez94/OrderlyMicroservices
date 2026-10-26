namespace Kitchen.API.Tests.EventHandlers;

public sealed class KitchenCancellationConsumerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    private static KitchenTicket NewTestTicket()
    {
        var seed = new OrderItemSeed(
            OrderItemId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid(),
            MenuItemName: "Burger",
            Quantity: 1,
            UnitPrice: 9.99m,
            SelectedVariations: [],
            Customizations: [],
            SpecialInstructions: null,
            SeatNumber: null);

        var ticket = KitchenTicket.CreateFromOrder(
            orderId: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: "ORD-2026-0001",
            itemSeeds: [seed],
            notes: string.Empty,
            receivedAt: Now);

        ticket.Accept(Guid.NewGuid(), Now);
        return ticket;
    }

    [Fact]
    public async Task OrderCancelRequested_MarksTicketAndBroadcasts()
    {
        var ticket = NewTestTicket();
        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var broadcaster = Substitute.For<IKitchenTicketBroadcaster>();
        var restaurantProvider = Substitute.For<ICurrentRestaurantProvider>();

        var handler = new OrderCancelRequestedIntegrationEventHandler(
            repo, unitOfWork, broadcaster, NullLogger<OrderCancelRequestedIntegrationEventHandler>.Instance, restaurantProvider);

        var context = Substitute.For<ConsumeContext<OrderCancelRequestedIntegrationEvent>>();
        context.Message.Returns(new OrderCancelRequestedIntegrationEvent
        {
            OrderId = ticket.Id.Value,
            RestaurantId = ticket.RestaurantId,
            OrderNumber = ticket.OrderNumber,
            RequestedAt = Now,
            RequestedByUserId = Guid.NewGuid(),
            Reason = "Customer changed mind",
        });
        context.CancellationToken.Returns(CancellationToken.None);

        await handler.Consume(context);

        ticket.IsCancelRequested.Should().BeTrue();
        ticket.CancelRequestReason.Should().Be("Customer changed mind");
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await broadcaster.Received(1).BroadcastTicketUpdatedAsync(ticket, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderCancelRequested_WhenAlreadyCancelled_Skips()
    {
        var ticket = NewTestTicket();
        ticket.Cancel("Kitchen cancelled", Guid.NewGuid(), Now);

        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var broadcaster = Substitute.For<IKitchenTicketBroadcaster>();

        var handler = new OrderCancelRequestedIntegrationEventHandler(
            repo, unitOfWork, broadcaster, NullLogger<OrderCancelRequestedIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<OrderCancelRequestedIntegrationEvent>>();
        context.Message.Returns(new OrderCancelRequestedIntegrationEvent
        {
            OrderId = ticket.Id.Value,
            RestaurantId = ticket.RestaurantId,
            OrderNumber = ticket.OrderNumber,
            RequestedAt = Now,
            RequestedByUserId = Guid.NewGuid(),
            Reason = "Too late",
        });

        await handler.Consume(context);

        ticket.IsCancelRequested.Should().BeFalse();
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await broadcaster.DidNotReceive().BroadcastTicketUpdatedAsync(Arg.Any<KitchenTicket>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderCancelRejected_ClearsRequestAndBroadcasts()
    {
        var ticket = NewTestTicket();
        ticket.MarkCancelRequested(Now, "Reason");

        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var broadcaster = Substitute.For<IKitchenTicketBroadcaster>();

        var handler = new OrderCancelRejectedIntegrationEventHandler(
            repo, unitOfWork, broadcaster, NullLogger<OrderCancelRejectedIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<OrderCancelRejectedIntegrationEvent>>();
        context.Message.Returns(new OrderCancelRejectedIntegrationEvent
        {
            OrderId = ticket.Id.Value,
            RestaurantId = ticket.RestaurantId,
            OrderNumber = ticket.OrderNumber,
            RejectedAt = Now,
            RejectedByUserId = Guid.NewGuid(),
            Reason = "Denied",
            RestoredStatus = "Confirmed",
        });

        await handler.Consume(context);

        ticket.IsCancelRequested.Should().BeFalse();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await broadcaster.Received(1).BroadcastTicketUpdatedAsync(ticket, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderCancelled_MovesToCancelledPendingAck_AndBroadcasts()
    {
        var ticket = NewTestTicket();

        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var broadcaster = Substitute.For<IKitchenTicketBroadcaster>();

        var handler = new OrderCancelledIntegrationEventHandler(
            repo, unitOfWork, broadcaster, NullLogger<OrderCancelledIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<OrderCancelledIntegrationEvent>>();
        context.Message.Returns(new OrderCancelledIntegrationEvent
        {
            OrderId = ticket.Id.Value,
            RestaurantId = ticket.RestaurantId,
            OrderNumber = ticket.OrderNumber,
            CancelledAt = Now,
            CancelledByUserId = Guid.NewGuid(),
            Reason = "External cancel",
        });

        await handler.Consume(context);

        ticket.Status.Should().Be(KitchenTicketStatus.CancelledPendingAck);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await broadcaster.Received(1).BroadcastTicketUpdatedAsync(ticket, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderCancelled_WhenAlreadyCancelled_ReturnsImmediatelyToPreventSelfAckLoop()
    {
        var ticket = NewTestTicket();
        ticket.Cancel("Kitchen cancelled", Guid.NewGuid(), Now);

        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var broadcaster = Substitute.For<IKitchenTicketBroadcaster>();

        var handler = new OrderCancelledIntegrationEventHandler(
            repo, unitOfWork, broadcaster, NullLogger<OrderCancelledIntegrationEventHandler>.Instance);

        var context = Substitute.For<ConsumeContext<OrderCancelledIntegrationEvent>>();
        context.Message.Returns(new OrderCancelledIntegrationEvent
        {
            OrderId = ticket.Id.Value,
            RestaurantId = ticket.RestaurantId,
            OrderNumber = ticket.OrderNumber,
            CancelledAt = Now,
            CancelledByUserId = Guid.NewGuid(),
            Reason = "Echo from ordering",
        });

        await handler.Consume(context);

        ticket.Status.Should().Be(KitchenTicketStatus.Cancelled);
        ticket.CancellationReason.Should().Be("Kitchen cancelled");
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await broadcaster.DidNotReceive().BroadcastTicketUpdatedAsync(Arg.Any<KitchenTicket>(), Arg.Any<CancellationToken>());
    }
}
