namespace Kitchen.API.Tests.Hubs;

/// <summary>
/// Locks in the broadcast mapping for every domain event the
/// <c>KitchenTicket</c> aggregate emits. Each event must land on the
/// matching client method, addressed at <c>restaurant:{id}</c> so only
/// staff in the same restaurant receive the update.
/// </summary>
public sealed class KitchenTicketBroadcasterTests
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

    /// <summary>
    /// Builds a broadcaster and a substitute <see cref="IKitchenHubClient"/>
    /// proxy that's pre-wired for a single <c>restaurant:{id}</c> group call.
    /// Returns the proxy so tests can verify which method was called.
    /// </summary>
    private static (KitchenTicketBroadcaster broadcaster, IKitchenHubClient clientProxy, Guid restaurantId)
        BuildBroadcaster()
    {
        var ticket = NewTicket();

        var clientProxy = Substitute.For<IKitchenHubClient>();
        var clientsProxy = Substitute.For<IHubClients<IKitchenHubClient>>();
        // SignalR's IHubClients<T>.Group returns T for any group name; using
        // ReturnsForAnyArgs avoids Arg.Is<T>(predicate) edge cases across overloads.
        clientsProxy.Group(default!).ReturnsForAnyArgs(clientProxy);

        var hub = Substitute.For<IHubContext<KitchenHub, IKitchenHubClient>>();
        hub.Clients.Returns(clientsProxy);

        return (new KitchenTicketBroadcaster(hub), clientProxy, ticket.RestaurantId);
    }

    [Fact]
    public async Task Accepted_BroadcastsTicketAccepted()
    {
        var (broadcaster, client, _) = BuildBroadcaster();
        var ticket = NewTicket();
        var staffId = Guid.NewGuid();

        await broadcaster.Handle(
            new KitchenTicketAcceptedEvent(ticket, staffId, Now),
            CancellationToken.None);

        await client.Received(1).TicketAccepted(ticket.Id.Value, staffId, Now);
    }

    [Fact]
    public async Task ItemPrepStarted_BroadcastsItemStateChanged_Preparing()
    {
        var (broadcaster, client, _) = BuildBroadcaster();
        var ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Now);
        var itemId = ticket.Items.First().Id;

        await broadcaster.Handle(
            new KitchenTicketItemPrepStartedEvent(ticket, itemId, Now),
            CancellationToken.None);

        await client.Received(1).ItemStateChanged(ticket.Id.Value, itemId.Value, nameof(KitchenItemStatus.Preparing));
    }

    [Fact]
    public async Task ItemReady_BroadcastsItemStateChanged_Ready()
    {
        var (broadcaster, client, _) = BuildBroadcaster();
        var ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Now);
        var itemId = ticket.Items.First().Id;
        ticket.StartItemPrep(itemId, Now);

        await broadcaster.Handle(
            new KitchenTicketItemReadyEvent(ticket, itemId, Now),
            CancellationToken.None);

        await client.Received(1).ItemStateChanged(ticket.Id.Value, itemId.Value, nameof(KitchenItemStatus.Ready));
    }

    [Fact]
    public async Task Ready_BroadcastsOrderReady()
    {
        var (broadcaster, client, _) = BuildBroadcaster();
        var ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Now);
        foreach (var item in ticket.Items)
        {
            ticket.StartItemPrep(item.Id, Now);
            ticket.MarkItemReady(item.Id, Now);
        }
        ticket.MarkReady(Now);

        await broadcaster.Handle(
            new KitchenTicketReadyEvent(ticket, Now),
            CancellationToken.None);

        await client.Received(1).OrderReady(ticket.Id.Value, Now);
    }

    [Fact]
    public async Task Bumped_BroadcastsOrderBumped()
    {
        var (broadcaster, client, _) = BuildBroadcaster();
        var ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Now);
        foreach (var item in ticket.Items)
        {
            ticket.StartItemPrep(item.Id, Now);
            ticket.MarkItemReady(item.Id, Now);
        }
        ticket.MarkReady(Now);
        ticket.Bump(Now);

        await broadcaster.Handle(
            new KitchenTicketBumpedEvent(ticket, Now),
            CancellationToken.None);

        await client.Received(1).OrderBumped(ticket.Id.Value, Now);
    }

    [Fact]
    public async Task Cancelled_BroadcastsOrderCancelledWithReason()
    {
        var (broadcaster, client, _) = BuildBroadcaster();
        var ticket = NewTicket();
        var reason = "customer changed mind";
        ticket.Cancel(reason, Guid.NewGuid(), Now);

        await broadcaster.Handle(
            new KitchenTicketCancelledEvent(ticket, reason, Guid.NewGuid(), Now),
            CancellationToken.None);

        await client.Received(1).OrderCancelled(ticket.Id.Value, reason);
    }

    [Fact]
    public async Task Recalled_BroadcastsTicketRecalled()
    {
        var (broadcaster, client, _) = BuildBroadcaster();
        var ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Now);
        foreach (var item in ticket.Items)
        {
            ticket.StartItemPrep(item.Id, Now);
            ticket.MarkItemReady(item.Id, Now);
        }
        ticket.MarkReady(Now);
        ticket.Bump(Now);
        ticket.Recall(Now);

        await broadcaster.Handle(
            new KitchenTicketRecalledEvent(ticket, Now),
            CancellationToken.None);

        await client.Received(1).TicketRecalled(ticket.Id.Value);
    }
}