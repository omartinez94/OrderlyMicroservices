namespace Kitchen.API.Tests.Commands;

public sealed class RejectCancelRequestHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    private static KitchenTicket TicketWithCancelRequest()
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
        ticket.MarkCancelRequested(Now, "Customer changed mind");
        return ticket;
    }

    [Fact]
    public async Task Handle_WhenCancelRequested_ClearsRequest_PublishesIntegrationEvent_AndBroadcasts()
    {
        var ticket = TicketWithCancelRequest();
        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var publish = Substitute.For<IOutboxPublisher>();
        var currentUser = Substitute.For<ICurrentUser>();
        var staffId = Guid.NewGuid();
        currentUser.UserId.Returns(staffId);

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var broadcaster = Substitute.For<IKitchenTicketBroadcaster>();

        var handler = new RejectCancelRequestHandler(
            repo, unitOfWork, publish, currentUser, broadcaster, NullLogger<RejectCancelRequestHandler>.Instance);

        var result = await handler.Handle(
            new RejectCancelRequestCommand(ticket.Id.Value, "Cannot cancel; already cooking"),
            CancellationToken.None);

        result.TicketId.Should().Be(ticket.Id.Value);
        ticket.IsCancelRequested.Should().BeFalse();
        ticket.CancelRequestReason.Should().BeNull();

        await publish.Received(1).PublishAsync(
            Arg.Is<KitchenOrderCancelRejectedIntegrationEvent>(e =>
                e.OrderId == ticket.Id.Value &&
                e.RestaurantId == ticket.RestaurantId &&
                e.OrderNumber == ticket.OrderNumber &&
                e.Reason == "Cannot cancel; already cooking" &&
                e.RejectedByUserId == staffId.ToString()),
            Arg.Any<CancellationToken>());

        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await broadcaster.Received(1).BroadcastTicketUpdatedAsync(ticket, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTicketNotFound_ThrowsKitchenTicketNotFoundException()
    {
        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((KitchenTicket?)null);

        var handler = new RejectCancelRequestHandler(
            repo,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IOutboxPublisher>(),
            Substitute.For<ICurrentUser>(),
            Substitute.For<IKitchenTicketBroadcaster>(),
            NullLogger<RejectCancelRequestHandler>.Instance);

        Func<Task> act = () => handler.Handle(
            new RejectCancelRequestCommand(Guid.NewGuid(), "reason"),
            CancellationToken.None);

        await act.Should().ThrowAsync<KitchenTicketNotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenCancelNotRequested_ThrowsKitchenDomainException()
    {
        var ticket = TicketWithCancelRequest();
        ticket.ClearCancelRequest(); // Cancel not active

        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var handler = new RejectCancelRequestHandler(
            repo,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IOutboxPublisher>(),
            Substitute.For<ICurrentUser>(),
            Substitute.For<IKitchenTicketBroadcaster>(),
            NullLogger<RejectCancelRequestHandler>.Instance);

        Func<Task> act = () => handler.Handle(
            new RejectCancelRequestCommand(ticket.Id.Value, "reason"),
            CancellationToken.None);

        await act.Should().ThrowAsync<KitchenDomainException>();
    }
}
