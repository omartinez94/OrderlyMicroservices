namespace Kitchen.API.Tests.Commands;

/// <summary>
/// Contract: <see cref="StartItemPrepHandler"/> publishes
/// <see cref="KitchenOrderPrepStartedIntegrationEvent"/> exactly once per
/// ticket — on the first item-start action that moves the aggregate out of
/// the "no item preparing yet" state. Subsequent item-starts on the same
/// ticket must NOT re-publish (the predicate <c>StartedAt is null</c> only
/// holds once). Ordering's <c>MarkPreparing</c> is idempotent in effect but
/// the duplicate publish is what we want to guard against here.
/// </summary>
public sealed class StartItemPrepHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    /// <summary>
    /// Builds a <c>New</c> ticket with two items so we can exercise the
    /// "first start publishes, second start does not" sequence in a single
    /// test.
    /// </summary>
    private static (KitchenTicket ticket, Guid firstItemId, Guid secondItemId) NewTwoItemTicket()
    {
        var first = new OrderItemSeed(
            OrderItemId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid(),
            MenuItemName: "Burger",
            Quantity: 1,
            UnitPrice: 9.99m,
            SelectedVariations: [],
            Customizations: [],
            SpecialInstructions: null,
            SeatNumber: null);

        var second = new OrderItemSeed(
            OrderItemId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid(),
            MenuItemName: "Fries",
            Quantity: 1,
            UnitPrice: 3.50m,
            SelectedVariations: [],
            Customizations: [],
            SpecialInstructions: null,
            SeatNumber: null);

        var ticket = KitchenTicket.CreateFromOrder(
            orderId: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: "ORD-2026-0001",
            itemSeeds: [first, second],
            notes: string.Empty,
            receivedAt: Now);

        return (ticket, first.OrderItemId, second.OrderItemId);
    }

    private static StartItemPrepHandler BuildHandler(
        KitchenTicket ticket,
        Guid staffUserId,
        out IPublishEndpoint publishEndpoint)
    {
        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var uow = Substitute.For<IUnitOfWork>();
        publishEndpoint = Substitute.For<IPublishEndpoint>();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(staffUserId);

        return new StartItemPrepHandler(
            repo, uow, publishEndpoint, currentUser, NullLogger<StartItemPrepHandler>.Instance);
    }

    [Fact]
    public async Task Handle_FirstItemStart_PublishesKitchenOrderPrepStartedIntegrationEvent()
    {
        var (ticket, firstItemId, _) = NewTwoItemTicket();
        var staffId = Guid.NewGuid();
        var handler = BuildHandler(ticket, staffId, out var publish);

        var result = await handler.Handle(
            new StartItemPrepCommand(ticket.Id.Value, firstItemId),
            CancellationToken.None);

        result.FirstItemStarted.Should().BeTrue();

        var publishes = publish.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IPublishEndpoint.Publish))
            .ToList();
        publishes.Should().HaveCount(1);

        var evt = (KitchenOrderPrepStartedIntegrationEvent)publishes[0].GetArguments()[0]!;
        evt.OrderId.Should().Be(ticket.Id.Value);
        evt.ItemId.Should().Be(firstItemId);
        evt.StaffUserId.Should().Be(staffId);
    }

    [Fact]
    public async Task Handle_SecondItemStart_DoesNotPublishIntegrationEvent()
    {
        var (ticket, firstItemId, secondItemId) = NewTwoItemTicket();
        var staffId = Guid.NewGuid();

        // First call: the publish happens exactly once. The handler is
        // re-built between calls so the substitute's call-counter starts at
        // zero each time and we can assert the second call alone did NOT
        // publish.
        var firstHandler = BuildHandler(ticket, staffId, out var firstPublish);
        await firstHandler.Handle(
            new StartItemPrepCommand(ticket.Id.Value, firstItemId),
            CancellationToken.None);
        firstPublish.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IPublishEndpoint.Publish))
            .Should().Be(1);

        var secondHandler = BuildHandler(ticket, staffId, out var secondPublish);
        var secondResult = await secondHandler.Handle(
            new StartItemPrepCommand(ticket.Id.Value, secondItemId),
            CancellationToken.None);

        secondResult.FirstItemStarted.Should().BeFalse();
        _ = secondPublish.DidNotReceiveWithAnyArgs().Publish(default!);
    }

    [Fact]
    public async Task Handle_TwoItemStartsOnSameTicket_PublishExactlyOnce()
    {
        // End-to-end on the same ticket aggregate: first start publishes,
        // second start does not. This is the integration-test shape that
        // proves the predicate lives on the aggregate (not on transient
        // handler state).
        var (ticket, firstItemId, secondItemId) = NewTwoItemTicket();
        var staffId = Guid.NewGuid();
        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var uow = Substitute.For<IUnitOfWork>();
        var publish = Substitute.For<IPublishEndpoint>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(staffId);

        var handler = new StartItemPrepHandler(
            repo, uow, publish, currentUser, NullLogger<StartItemPrepHandler>.Instance);

        await handler.Handle(
            new StartItemPrepCommand(ticket.Id.Value, firstItemId),
            CancellationToken.None);
        await handler.Handle(
            new StartItemPrepCommand(ticket.Id.Value, secondItemId),
            CancellationToken.None);

        var publishes = publish.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IPublishEndpoint.Publish))
            .ToList();
        publishes.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WhenUserUnauthenticated_Throws()
    {
        var (ticket, firstItemId, _) = NewTwoItemTicket();
        var handler = BuildHandler(ticket, Guid.Empty, out _);

        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var unauthHandler = new StartItemPrepHandler(
            repo,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IPublishEndpoint>(),
            currentUser,
            NullLogger<StartItemPrepHandler>.Instance);

        Func<Task> act = () => unauthHandler.Handle(
            new StartItemPrepCommand(ticket.Id.Value, firstItemId),
            CancellationToken.None);

        await act.Should().ThrowAsync<KitchenDomainException>();
    }

    [Fact]
    public async Task Handle_WhenTicketMissing_ThrowsNotFound()
    {
        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((KitchenTicket?)null);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());

        var handler = new StartItemPrepHandler(
            repo,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IPublishEndpoint>(),
            currentUser,
            NullLogger<StartItemPrepHandler>.Instance);

        Func<Task> act = () => handler.Handle(
            new StartItemPrepCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<KitchenTicketNotFoundException>();
    }
}