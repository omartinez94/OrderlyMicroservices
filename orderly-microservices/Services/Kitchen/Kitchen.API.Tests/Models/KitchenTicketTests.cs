namespace Kitchen.API.Tests.Models;

/// <summary>
/// Locks in the legal-transition table for <see cref="KitchenTicket"/> and
/// rejects every illegal transition with the right exception. Mirrors the
/// aggregate-style coverage that <c>Ordering.Domain.Tests/OrderTests.cs</c>
/// provides for the <c>Order</c> aggregate.
/// </summary>
public sealed class KitchenTicketTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);
    private static readonly Instant Later = Instant.FromUtc(2026, 7, 4, 12, 15);

    private static KitchenTicket NewTicket(int itemCount = 2)
    {
        var seeds = Enumerable.Range(0, itemCount)
            .Select(i => new OrderItemSeed(
                OrderItemId: Guid.NewGuid(),
                MenuItemId: Guid.NewGuid(),
                MenuItemName: $"Item {i}",
                Quantity: 1,
                UnitPrice: 9.99m,
                SelectedVariations: [],
                Customizations: [],
                SpecialInstructions: null,
                SeatNumber: null))
            .ToList();

        return KitchenTicket.CreateFromOrder(
            orderId: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: "ORD-2026-0001",
            itemSeeds: seeds,
            notes: string.Empty,
            receivedAt: Now);
    }

    private static KitchenItemId ItemId(KitchenTicket ticket, int index = 0) =>
        ticket.Items.ElementAt(index).Id;

    // -------- CreateFromOrder --------

    [Fact]
    public void CreateFromOrder_PopulatesFieldsAndStartsAsNew()
    {
        var orderId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var ticket = NewTicket(itemCount: 3);

        ticket.Status.Should().Be(KitchenTicketStatus.New);
        ticket.Items.Should().HaveCount(3);
        ticket.ReceivedAt.Should().Be(Now);
        ticket.StartedAt.Should().BeNull();
        ticket.ReadyAt.Should().BeNull();
    }

    [Fact]
    public void CreateFromOrder_WithEmptyOrderId_Throws()
    {
        Action act = () => KitchenTicket.CreateFromOrder(
            orderId: Guid.Empty,
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: "ORD-2026-0001",
            itemSeeds: [],
            notes: string.Empty,
            receivedAt: Now);

        act.Should().Throw<ArgumentException>().WithParameterName("orderId");
    }

    [Fact]
    public void CreateFromOrder_WithEmptyOrderNumber_Throws()
    {
        Action act = () => KitchenTicket.CreateFromOrder(
            orderId: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: "",
            itemSeeds: [],
            notes: string.Empty,
            receivedAt: Now);

        act.Should().Throw<ArgumentException>();
    }

    // -------- Accept --------

    [Fact]
    public void Accept_FromNew_TransitionsToInProgress_AndRaisesEvent()
    {
        var ticket = NewTicket();
        var userId = Guid.NewGuid();

        ticket.Accept(userId, Later);

        ticket.Status.Should().Be(KitchenTicketStatus.InProgress);
        ticket.ConfirmedByUserId.Should().Be(userId);
        ticket.StartedAt.Should().Be(Later);
        ticket.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<KitchenTicketAcceptedEvent>();
    }

    [Fact]
    public void Accept_FromInProgress_Throws()
    {
        var ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Later);

        Action act = () => ticket.Accept(Guid.NewGuid(), Later);
        act.Should().Throw<InvalidKitchenTicketStateTransitionException>()
            .Which.AttemptedTransition.Should().Be(nameof(KitchenTicket.Accept));
    }

    [Fact]
    public void Accept_FromReady_Throws()
    {
        var ticket = ReadyTicket();
        Action act = () => ticket.Accept(Guid.NewGuid(), Later);
        act.Should().Throw<InvalidKitchenTicketStateTransitionException>();
    }

    [Fact]
    public void Accept_WithEmptyUserId_Throws()
    {
        var ticket = NewTicket();
        Action act = () => ticket.Accept(Guid.Empty, Later);
        act.Should().Throw<ArgumentException>().WithParameterName("staffUserId");
    }

    // -------- StartItemPrep --------

    [Fact]
    public void StartItemPrep_OnPendingItem_MovesItemToPreparing()
    {
        var ticket = NewTicket();
        var itemId = ItemId(ticket);

        ticket.StartItemPrep(itemId, Later);

        var item = ticket.Items.Single(i => i.Id == itemId);
        item.Status.Should().Be(KitchenItemStatus.Preparing);
        item.StartedAt.Should().Be(Later);
        ticket.Status.Should().Be(KitchenTicketStatus.New);
        ticket.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<KitchenTicketItemPrepStartedEvent>();
    }

    [Fact]
    public void StartItemPrep_AlreadyPreparing_ItemThrows()
    {
        var ticket = NewTicket();
        var itemId = ItemId(ticket);
        ticket.StartItemPrep(itemId, Later);

        Action act = () => ticket.StartItemPrep(itemId, Later);
        act.Should().Throw<InvalidKitchenItemStateTransitionException>();
    }

    // -------- MarkItemReady --------

    [Fact]
    public void MarkItemReady_OnPreparing_MovesItemToReady()
    {
        var ticket = NewTicket();
        var itemId = ItemId(ticket);
        ticket.StartItemPrep(itemId, Later);

        ticket.MarkItemReady(itemId, Later);

        ticket.Items.Single(i => i.Id == itemId).Status
            .Should().Be(KitchenItemStatus.Ready);
        ticket.DomainEvents.OfType<KitchenTicketItemReadyEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void MarkItemReady_OnPending_ItemThrows()
    {
        var ticket = NewTicket();
        Action act = () => ticket.MarkItemReady(ItemId(ticket), Later);
        act.Should().Throw<InvalidKitchenItemStateTransitionException>();
    }

    // -------- MarkReady --------

    [Fact]
    public void MarkReady_WhenAllItemsReady_TransitionsToReady()
    {
        var ticket = AcceptedAndStarted();
        foreach (var item in ticket.Items)
        {
            ticket.StartItemPrep(item.Id, Later);
            ticket.MarkItemReady(item.Id, Later);
        }

        ticket.MarkReady(Later);

        ticket.Status.Should().Be(KitchenTicketStatus.Ready);
        ticket.ReadyAt.Should().Be(Later);
        ticket.DomainEvents.OfType<KitchenTicketReadyEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void MarkReady_WhenAnyItemStillPreparing_Throws()
    {
        var ticket = AcceptedAndStarted();
        var firstItemId = ItemId(ticket, 0);
        ticket.StartItemPrep(firstItemId, Later);
        ticket.MarkItemReady(firstItemId, Later);

        Action act = () => ticket.MarkReady(Later);
        act.Should().Throw<KitchenDomainException>();
    }

    [Fact]
    public void MarkReady_FromNew_Throws()
    {
        var ticket = NewTicket();
        Action act = () => ticket.MarkReady(Later);
        act.Should().Throw<InvalidKitchenTicketStateTransitionException>();
    }

    // -------- Bump --------

    [Fact]
    public void Bump_FromReady_TransitionsToBumped()
    {
        var ticket = ReadyTicket();

        ticket.Bump(Later);

        ticket.Status.Should().Be(KitchenTicketStatus.Bumped);
        ticket.BumpedAt.Should().Be(Later);
    }

    [Fact]
    public void Bump_FromInProgress_Throws()
    {
        var ticket = AcceptedAndStarted();
        Action act = () => ticket.Bump(Later);
        act.Should().Throw<InvalidKitchenTicketStateTransitionException>();
    }

    // -------- Recall --------

    [Fact]
    public void Recall_FromBumped_TransitionsBackToReady()
    {
        var ticket = ReadyTicket();
        ticket.Bump(Later);

        ticket.Recall(Later);

        ticket.Status.Should().Be(KitchenTicketStatus.Ready);
        ticket.BumpedAt.Should().BeNull();
    }

    [Fact]
    public void Recall_FromReady_Throws()
    {
        var ticket = ReadyTicket();
        Action act = () => ticket.Recall(Later);
        act.Should().Throw<InvalidKitchenTicketStateTransitionException>();
    }

    // -------- Cancel --------

    [Fact]
    public void Cancel_FromNew_TransitionsToCancelled()
    {
        var ticket = NewTicket();
        var userId = Guid.NewGuid();

        ticket.Cancel("customer changed mind", userId, Later);

        ticket.Status.Should().Be(KitchenTicketStatus.Cancelled);
        ticket.CancellationReason.Should().Be("customer changed mind");
        ticket.CancelledByUserId.Should().Be(userId);
        ticket.CancelledAt.Should().Be(Later);
    }

    [Fact]
    public void Cancel_FromReady_TransitionsToCancelled()
    {
        var ticket = ReadyTicket();
        ticket.Cancel("out of stock", Guid.NewGuid(), Later);
        ticket.Status.Should().Be(KitchenTicketStatus.Cancelled);
    }

    [Fact]
    public void Cancel_FromAlreadyCancelled_Throws()
    {
        var ticket = NewTicket();
        ticket.Cancel("first reason", Guid.NewGuid(), Later);

        Action act = () => ticket.Cancel("second reason", Guid.NewGuid(), Later);
        act.Should().Throw<InvalidKitchenTicketStateTransitionException>();
    }

    [Fact]
    public void Cancel_WithEmptyReason_Throws()
    {
        var ticket = NewTicket();
        Action act = () => ticket.Cancel("", Guid.NewGuid(), Later);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Cancel_WithEmptyUserId_Throws()
    {
        var ticket = NewTicket();
        Action act = () => ticket.Cancel("reason", Guid.Empty, Later);
        act.Should().Throw<ArgumentException>().WithParameterName("userId");
    }

    // -------- helpers --------

    private static KitchenTicket AcceptedAndStarted()
    {
        var ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Later);
        return ticket;
    }

    private static KitchenTicket ReadyTicket()
    {
        var ticket = AcceptedAndStarted();
        foreach (var item in ticket.Items)
        {
            ticket.StartItemPrep(item.Id, Later);
            ticket.MarkItemReady(item.Id, Later);
        }
        ticket.MarkReady(Later);
        return ticket;
    }
}