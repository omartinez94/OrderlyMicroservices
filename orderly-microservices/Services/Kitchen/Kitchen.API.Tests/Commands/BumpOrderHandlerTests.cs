namespace Kitchen.API.Tests.Commands;

public sealed class BumpOrderHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    private static KitchenTicket ReadyTicket()
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
        foreach (var item in ticket.Items)
        {
            ticket.StartItemPrep(item.Id, Now);
            ticket.MarkItemReady(item.Id, Now);
        }
        ticket.MarkReady(Now);
        return ticket;
    }

    [Fact]
    public async Task Handle_PublishesKitchenOrderBumpedIntegrationEvent()
    {
        var ticket = ReadyTicket();
        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var publish = Substitute.For<IOutboxPublisher>();
        var currentUser = Substitute.For<ICurrentUser>();
        var staffId = Guid.NewGuid();
        currentUser.UserId.Returns(staffId);

        var handler = new BumpOrderHandler(
            repo, Substitute.For<IUnitOfWork>(), publish, currentUser, NullLogger<BumpOrderHandler>.Instance);

        await handler.Handle(new BumpOrderCommand(ticket.Id.Value), CancellationToken.None);

        var call = publish.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IOutboxPublisher.PublishAsync));
        var evt = (KitchenOrderBumpedIntegrationEvent)call.GetArguments()[0]!;

        evt.OrderId.Should().Be(ticket.Id.Value);
        evt.BumpedByUserId.Should().Be(staffId);
    }

    [Fact]
    public async Task Handle_WhenTicketNotReady_Throws()
    {
        // Build a ticket that is only Accepted, not Ready.
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

        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var publish = Substitute.For<IOutboxPublisher>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());

        var handler = new BumpOrderHandler(
            repo, Substitute.For<IUnitOfWork>(), publish, currentUser, NullLogger<BumpOrderHandler>.Instance);

        Func<Task> act = () => handler.Handle(new BumpOrderCommand(ticket.Id.Value), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidKitchenTicketStateTransitionException>();
    }
}