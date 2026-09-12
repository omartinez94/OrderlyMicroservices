namespace Kitchen.API.Tests.Commands;

/// <summary>
/// Covers per-item completion: <c>Preparing -&gt; Ready</c> on a single
/// <see cref="KitchenTicketItem"/>.
///
/// <para><b>Plan deviation (E2E_TEST_COVERAGE_PLAN §6.4).</b> The plan lists
/// <c>Handle_AllItemsReady_RaisesAllItemsReadyFlag()</c>. The aggregate has no
/// such flag: marking the last item ready deliberately leaves the ticket in
/// <c>InProgress</c>, and only the separate aggregate-level
/// <c>MarkOrderReady</c> command flips it to <c>Ready</c>. That test is
/// replaced by <see cref="Handle_LastItemReady_LeavesTicketInProgress"/>,
/// which asserts the real contract.</para>
/// </summary>
public sealed class MarkItemReadyHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    private static KitchenTicket NewTicket(int itemCount = 1)
    {
        List<OrderItemSeed> seeds = [];
        for (int i = 0; i < itemCount; i++)
        {
            seeds.Add(new OrderItemSeed(
                OrderItemId: Guid.NewGuid(),
                MenuItemId: Guid.NewGuid(),
                MenuItemName: $"Burger {i}",
                Quantity: 1,
                UnitPrice: 9.99m,
                SelectedVariations: [],
                Customizations: [],
                SpecialInstructions: null,
                SeatNumber: null));
        }

        return KitchenTicket.CreateFromOrder(
            orderId: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: "ORD-2026-0001",
            itemSeeds: seeds,
            notes: string.Empty,
            receivedAt: Now);
    }

    private static (MarkItemReadyHandler handler, IUnitOfWork unitOfWork) BuildHandler(KitchenTicket? ticket)
    {
        IKitchenTicketRepository repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ticket);

        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();

        return (
            new MarkItemReadyHandler(repo, unitOfWork, NullLogger<MarkItemReadyHandler>.Instance),
            unitOfWork);
    }

    [Fact]
    public async Task Handle_PreparingItem_MarksReady()
    {
        KitchenTicket ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Now);
        KitchenTicketItem item = ticket.Items.First();
        ticket.StartItemPrep(item.Id, Now);

        (MarkItemReadyHandler handler, IUnitOfWork unitOfWork) = BuildHandler(ticket);

        MarkItemReadyResult result = await handler.Handle(
            new MarkItemReadyCommand(ticket.Id.Value, item.Id.Value),
            CancellationToken.None);

        result.TicketId.Should().Be(ticket.Id.Value);
        result.ItemId.Should().Be(item.Id.Value);
        item.Status.Should().Be(KitchenItemStatus.Ready);
        item.ReadyAt.Should().NotBeNull();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PendingItem_Throws()
    {
        // The item was never started, so Pending -> Ready is illegal.
        KitchenTicket ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Now);
        KitchenTicketItem item = ticket.Items.First();

        (MarkItemReadyHandler handler, IUnitOfWork unitOfWork) = BuildHandler(ticket);

        Func<Task> act = () => handler.Handle(
            new MarkItemReadyCommand(ticket.Id.Value, item.Id.Value),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidKitchenItemStateTransitionException>();
        item.Status.Should().Be(KitchenItemStatus.Pending);
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Handle_LastItemReady_LeavesTicketInProgress()
    {
        // Marking every item ready must NOT flip the aggregate — only the
        // separate MarkOrderReady command does that.
        KitchenTicket ticket = NewTicket(itemCount: 2);
        ticket.Accept(Guid.NewGuid(), Now);
        foreach (KitchenTicketItem item in ticket.Items)
        {
            ticket.StartItemPrep(item.Id, Now);
        }

        (MarkItemReadyHandler handler, IUnitOfWork _) = BuildHandler(ticket);

        foreach (KitchenTicketItem item in ticket.Items)
        {
            await handler.Handle(
                new MarkItemReadyCommand(ticket.Id.Value, item.Id.Value),
                CancellationToken.None);
        }

        ticket.Items.Should().OnlyContain(i => i.Status == KitchenItemStatus.Ready);
        ticket.Status.Should().Be(KitchenTicketStatus.InProgress);
        ticket.ReadyAt.Should().BeNull();
        ticket.DomainEvents.OfType<KitchenTicketItemReadyEvent>().Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_UnknownTicket_ThrowsKitchenTicketNotFound()
    {
        (MarkItemReadyHandler handler, IUnitOfWork _) = BuildHandler(ticket: null);

        Func<Task> act = () => handler.Handle(
            new MarkItemReadyCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<KitchenTicketNotFoundException>();
    }
}
