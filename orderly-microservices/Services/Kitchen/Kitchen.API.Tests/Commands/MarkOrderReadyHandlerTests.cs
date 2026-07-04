namespace Kitchen.API.Tests.Commands;

public sealed class MarkOrderReadyHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    private static KitchenTicket ReadyToMarkReady()
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
        return ticket;
    }

    [Fact]
    public async Task Handle_PublishesKitchenOrderReadyIntegrationEvent()
    {
        var ticket = ReadyToMarkReady();
        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var publish = Substitute.For<IPublishEndpoint>();
        var handler = new MarkOrderReadyHandler(
            repo, Substitute.For<IUnitOfWork>(), publish, NullLogger<MarkOrderReadyHandler>.Instance);

        await handler.Handle(new MarkOrderReadyCommand(ticket.Id.Value), CancellationToken.None);

        var call = publish.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IPublishEndpoint.Publish));
        var evt = (KitchenOrderReadyIntegrationEvent)call.GetArguments()[0]!;

        evt.OrderId.Should().Be(ticket.Id.Value);
        evt.ReadyAt.Should().NotBe(default);
    }

    [Fact]
    public async Task Handle_WhenAnyItemStillPreparing_Throws()
    {
        var ticket = ReadyToMarkReady();
        // Skip marking the first item ready.
        ticket.Items.First().GetType()
            .GetProperty(nameof(KitchenTicketItem.Status))!
            .SetValue(ticket.Items.First(), KitchenItemStatus.Preparing);

        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var publish = Substitute.For<IPublishEndpoint>();
        var handler = new MarkOrderReadyHandler(
            repo, Substitute.For<IUnitOfWork>(), publish, NullLogger<MarkOrderReadyHandler>.Instance);

        Func<Task> act = () => handler.Handle(new MarkOrderReadyCommand(ticket.Id.Value), CancellationToken.None);
        await act.Should().ThrowAsync<KitchenDomainException>();
        await publish.DidNotReceiveWithAnyArgs().Publish(default(object)!, default);
    }
}