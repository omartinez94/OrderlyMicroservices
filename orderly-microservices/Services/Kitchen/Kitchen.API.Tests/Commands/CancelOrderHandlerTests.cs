namespace Kitchen.API.Tests.Commands;

public sealed class CancelOrderHandlerTests
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

    [Fact]
    public async Task Handle_PublishesKitchenOrderCancelledIntegrationEvent()
    {
        var ticket = NewTicket();
        var repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(ticket.Id.Value, Arg.Any<CancellationToken>()).Returns(ticket);

        var publish = Substitute.For<IPublishEndpoint>();
        var currentUser = Substitute.For<ICurrentUser>();
        var staffId = Guid.NewGuid();
        currentUser.UserId.Returns(staffId);

        var handler = new CancelOrderHandler(
            repo, Substitute.For<IUnitOfWork>(), publish, currentUser, NullLogger<CancelOrderHandler>.Instance);

        await handler.Handle(new CancelOrderCommand(ticket.Id.Value, "customer changed mind"), CancellationToken.None);

        var call = publish.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IPublishEndpoint.Publish));
        var evt = (KitchenOrderCancelledIntegrationEvent)call.GetArguments()[0]!;

        evt.OrderId.Should().Be(ticket.Id.Value);
        evt.Reason.Should().Be("customer changed mind");
        evt.CancelledByUserId.Should().Be(staffId);
    }

    [Fact]
    public void Validator_RejectsEmptyReason()
    {
        var validator = new CancelOrderCommandValidator();
        var result = validator.Validate(new CancelOrderCommand(Guid.NewGuid(), ""));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CancelOrderCommand.Reason));
    }

    [Fact]
    public void Validator_RejectsOverlongReason()
    {
        var validator = new CancelOrderCommandValidator();
        var longReason = new string('x', 501);
        var result = validator.Validate(new CancelOrderCommand(Guid.NewGuid(), longReason));
        result.IsValid.Should().BeFalse();
    }
}