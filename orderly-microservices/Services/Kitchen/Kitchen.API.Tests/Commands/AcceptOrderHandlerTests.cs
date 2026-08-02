namespace Kitchen.API.Tests.Commands;

/// <summary>
/// Locks in the M3 contract: every command handler that drives an
/// aggregate-level transition publishes the matching outbound integration
/// event with the right payload — so Ordering can subscribe and apply its
/// own aggregate method. Per-item commands (StartItemPrep, MarkItemReady)
/// intentionally publish nothing; those are covered by aggregate-event
/// tests in <c>KitchenTicketTests</c>.
/// </summary>
public sealed class AcceptOrderHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    private static KitchenTicket NewTicket()
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

        return KitchenTicket.CreateFromOrder(
            orderId: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: "ORD-2026-0001",
            itemSeeds: [seed],
            notes: string.Empty,
            receivedAt: Now);
    }

    private static AcceptOrderHandler BuildHandler(
        KitchenTicket ticket,
        out IOutboxPublisher outboxPublisher,
        out ICurrentUser currentUser,
        Guid? userId = null)
    {
        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var uow = Substitute.For<IUnitOfWork>();
        outboxPublisher = Substitute.For<IOutboxPublisher>();
        currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId ?? Guid.NewGuid());

        return new AcceptOrderHandler(repo, uow, outboxPublisher, currentUser, NullLogger<AcceptOrderHandler>.Instance);
    }

    [Fact]
    public async Task Handle_PublishesKitchenOrderAcceptedIntegrationEvent()
    {
        var ticket = NewTicket();
        var handler = BuildHandler(ticket, out var publish, out _);
        var staffId = Guid.NewGuid();
        ((Substitute.For<ICurrentUser>()).UserId).Returns(staffId);

        // Re-wire currentUser so the handler resolves our explicit id.
        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(staffId);
        var freshHandler = new AcceptOrderHandler(
            repo, Substitute.For<IUnitOfWork>(), publish, currentUser, NullLogger<AcceptOrderHandler>.Instance);

        await freshHandler.Handle(new AcceptOrderCommand(ticket.Id.Value), CancellationToken.None);

        var call = publish.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IOutboxPublisher.PublishAsync));
        var evt = (KitchenOrderAcceptedIntegrationEvent)call.GetArguments()[0]!;

        evt.OrderId.Should().Be(ticket.Id.Value);
        evt.ConfirmedByUserId.Should().Be(staffId);
    }

    [Fact]
    public async Task Handle_WhenUserUnauthenticated_Throws()
    {
        var ticket = NewTicket();
        var handler = BuildHandler(ticket, out _, out var currentUser);
        currentUser.UserId.Returns((Guid?)null);

        Func<Task> act = () => handler.Handle(new AcceptOrderCommand(ticket.Id.Value), CancellationToken.None);
        await act.Should().ThrowAsync<KitchenDomainException>();
    }

    [Fact]
    public async Task Handle_WhenTicketMissing_ThrowsNotFound()
    {
        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((KitchenTicket?)null);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());

        var handler = new AcceptOrderHandler(
            repo,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IOutboxPublisher>(),
            currentUser,
            NullLogger<AcceptOrderHandler>.Instance);

        Func<Task> act = () => handler.Handle(new AcceptOrderCommand(Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<KitchenTicketNotFoundException>();
    }
}