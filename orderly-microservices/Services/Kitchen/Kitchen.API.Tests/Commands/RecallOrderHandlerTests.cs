using System.Reflection;

namespace Kitchen.API.Tests.Commands;

/// <summary>
/// Covers the chef pull-back command: <c>Bumped -&gt; Ready</c>.
/// </summary>
public sealed class RecallOrderHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    private static KitchenTicket NewTicket()
    {
        OrderItemSeed seed = new(
            OrderItemId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid(),
            MenuItemName: "Burger",
            Quantity: 1,
            UnitPrice: 9.99m,
            SelectedVariations: [],
            Customizations: [],
            SpecialInstructions: null,
            SeatNumber: null);

        return KitchenTicket.CreateFromOrder(
            orderId: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: "ORD-2026-0001",
            itemSeeds: [seed],
            notes: string.Empty,
            receivedAt: Now);
    }

    private static KitchenTicket ReadyTicket()
    {
        KitchenTicket ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Now);
        foreach (KitchenTicketItem item in ticket.Items)
        {
            ticket.StartItemPrep(item.Id, Now);
            ticket.MarkItemReady(item.Id, Now);
        }
        ticket.MarkReady(Now);
        return ticket;
    }

    private static KitchenTicket BumpedTicket()
    {
        KitchenTicket ticket = ReadyTicket();
        ticket.Bump(Now);
        return ticket;
    }

    private static (RecallOrderHandler handler, IUnitOfWork unitOfWork, IOutboxPublisher outboxPublisher, ICurrentUser currentUser) BuildHandler(
        KitchenTicket? ticket,
        Guid? staffUserId = null)
    {
        IKitchenTicketRepository repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ticket);

        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        IOutboxPublisher outboxPublisher = Substitute.For<IOutboxPublisher>();
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(staffUserId ?? Guid.NewGuid());

        RecallOrderHandler handler = new(
            repo,
            unitOfWork,
            outboxPublisher,
            currentUser,
            NullLogger<RecallOrderHandler>.Instance);

        return (handler, unitOfWork, outboxPublisher, currentUser);
    }

    [Fact]
    public async Task Handle_FromBumped_TransitionsToReady()
    {
        KitchenTicket ticket = BumpedTicket();
        (RecallOrderHandler handler, IUnitOfWork unitOfWork, _, _) = BuildHandler(ticket);

        RecallOrderResult result = await handler.Handle(
            new RecallOrderCommand(ticket.Id.Value),
            CancellationToken.None);

        result.TicketId.Should().Be(ticket.Id.Value);
        ticket.Status.Should().Be(KitchenTicketStatus.Ready);
        ticket.BumpedAt.Should().BeNull("recall clears the bump timestamp");
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PublishesKitchenOrderRecalledIntegrationEvent()
    {
        KitchenTicket ticket = BumpedTicket();
        Guid staffId = Guid.NewGuid();
        (RecallOrderHandler handler, _, IOutboxPublisher outbox, _) = BuildHandler(ticket, staffId);

        await handler.Handle(new RecallOrderCommand(ticket.Id.Value), CancellationToken.None);

        var call = outbox.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IOutboxPublisher.PublishAsync));
        KitchenOrderRecalledIntegrationEvent evt = (KitchenOrderRecalledIntegrationEvent)call.GetArguments()[0]!;

        evt.OrderId.Should().Be(ticket.Id.Value);
        evt.KitchenTicketId.Should().Be(ticket.Id.Value);
        evt.RestaurantId.Should().Be(ticket.RestaurantId);
        evt.RecalledByUserId.Should().Be(staffId);
    }

    [Fact]
    public async Task Handle_WithoutAuthenticatedUser_ThrowsKitchenDomainException()
    {
        KitchenTicket ticket = BumpedTicket();
        IKitchenTicketRepository repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ticket);

        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        RecallOrderHandler handler = new(
            repo,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IOutboxPublisher>(),
            currentUser,
            NullLogger<RecallOrderHandler>.Instance);

        Func<Task> act = () => handler.Handle(
            new RecallOrderCommand(ticket.Id.Value),
            CancellationToken.None);

        await act.Should().ThrowAsync<KitchenDomainException>()
            .WithMessage("*Authenticated staff user id is required*");
    }

    [Fact]
    public async Task Handle_FromReady_Throws()
    {
        KitchenTicket ticket = ReadyTicket();
        (RecallOrderHandler handler, IUnitOfWork unitOfWork, _, _) = BuildHandler(ticket);

        Func<Task> act = () => handler.Handle(
            new RecallOrderCommand(ticket.Id.Value),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidKitchenTicketStateTransitionException>();
        ticket.Status.Should().Be(KitchenTicketStatus.Ready);
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Handle_RaisesKitchenTicketRecalledDomainEvent()
    {
        KitchenTicket ticket = BumpedTicket();
        (RecallOrderHandler handler, _, _, _) = BuildHandler(ticket);

        await handler.Handle(new RecallOrderCommand(ticket.Id.Value), CancellationToken.None);

        ticket.DomainEvents.OfType<KitchenTicketRecalledEvent>()
            .Should().ContainSingle()
            .Which.Ticket.Id.Should().Be(ticket.Id);
    }

    [Fact]
    public async Task Handle_UnknownTicket_ThrowsKitchenTicketNotFound()
    {
        (RecallOrderHandler handler, _, _, _) = BuildHandler(ticket: null);

        Func<Task> act = () => handler.Handle(
            new RecallOrderCommand(Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<KitchenTicketNotFoundException>();
    }
}
