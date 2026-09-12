namespace Kitchen.API.Tests.Commands;

/// <summary>
/// Covers the chef pull-back command: <c>Bumped -&gt; Ready</c>.
///
/// <para><b>Plan deviation (E2E_TEST_COVERAGE_PLAN §6.4).</b> The plan lists
/// <c>Handle_PublishesKitchenOrderRecalledIntegrationEvent()</c>. No such
/// integration event exists: <c>RecallOrderHandler</c> takes neither
/// <c>IOutboxPublisher</c> nor <c>ICurrentUser</c> — recall is deliberately
/// local state only (Ordering does not react to it), and the in-process
/// <see cref="KitchenTicketRecalledEvent"/> is what drives the SignalR
/// broadcast. The third test therefore asserts the domain event is raised
/// instead of an integration event being published.</para>
/// </summary>
public sealed class RecallOrderHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    private static KitchenTicket NewTicket()
    {
        OrderItemSeed seed = new OrderItemSeed(
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

    private static (RecallOrderHandler handler, IUnitOfWork unitOfWork) BuildHandler(KitchenTicket? ticket)
    {
        IKitchenTicketRepository repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ticket);

        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();

        return (
            new RecallOrderHandler(repo, unitOfWork, NullLogger<RecallOrderHandler>.Instance),
            unitOfWork);
    }

    [Fact]
    public async Task Handle_FromBumped_TransitionsToReady()
    {
        KitchenTicket ticket = BumpedTicket();
        (RecallOrderHandler handler, IUnitOfWork unitOfWork) = BuildHandler(ticket);

        RecallOrderResult result = await handler.Handle(
            new RecallOrderCommand(ticket.Id.Value),
            CancellationToken.None);

        result.TicketId.Should().Be(ticket.Id.Value);
        ticket.Status.Should().Be(KitchenTicketStatus.Ready);
        ticket.BumpedAt.Should().BeNull("recall clears the bump timestamp");
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FromReady_Throws()
    {
        // Ready is not a legal source state for Recall — only Bumped is.
        KitchenTicket ticket = ReadyTicket();
        (RecallOrderHandler handler, IUnitOfWork unitOfWork) = BuildHandler(ticket);

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
        (RecallOrderHandler handler, IUnitOfWork _) = BuildHandler(ticket);

        await handler.Handle(new RecallOrderCommand(ticket.Id.Value), CancellationToken.None);

        ticket.DomainEvents.OfType<KitchenTicketRecalledEvent>()
            .Should().ContainSingle()
            .Which.Ticket.Id.Should().Be(ticket.Id);
    }

    [Fact]
    public async Task Handle_UnknownTicket_ThrowsKitchenTicketNotFound()
    {
        (RecallOrderHandler handler, IUnitOfWork _) = BuildHandler(ticket: null);

        Func<Task> act = () => handler.Handle(
            new RecallOrderCommand(Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<KitchenTicketNotFoundException>();
    }
}
