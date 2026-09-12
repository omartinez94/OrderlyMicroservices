using Kitchen.API.Application.Dtos;
using Kitchen.API.Application.KitchenTickets.Queries;

namespace Kitchen.API.Tests.Queries;

/// <summary>
/// Covers the read side of <c>GET /v1/kitchen/tickets/{id}</c>: the handler
/// loads the aggregate through the repository and projects it via
/// <c>KitchenTicketExtensions.ToDto</c>, or surfaces
/// <see cref="KitchenTicketNotFoundException"/> (mapped to 404 by the global
/// exception handler) when the id is unknown.
/// </summary>
public sealed class GetTicketByIdQueryHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    private static KitchenTicket NewTicket()
    {
        OrderItemSeed seed = new OrderItemSeed(
            OrderItemId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid(),
            MenuItemName: "Burger",
            Quantity: 2,
            UnitPrice: 9.99m,
            SelectedVariations: [],
            Customizations: [],
            SpecialInstructions: "no pickles",
            SeatNumber: 3);

        return KitchenTicket.CreateFromOrder(
            orderId: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: "ORD-2026-0001",
            itemSeeds: [seed],
            notes: "table by the window",
            receivedAt: Now);
    }

    private static GetTicketByIdHandler BuildHandler(KitchenTicket? ticket)
    {
        IKitchenTicketRepository repo = Substitute.For<IKitchenTicketRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ticket);
        return new GetTicketByIdHandler(repo);
    }

    [Fact]
    public async Task Handle_ExistingTicket_ReturnsDto()
    {
        KitchenTicket ticket = NewTicket();
        ticket.Accept(Guid.NewGuid(), Now);
        GetTicketByIdHandler handler = BuildHandler(ticket);

        KitchenTicketDto dto = await handler.Handle(
            new GetTicketByIdQuery(ticket.Id.Value),
            CancellationToken.None);

        dto.Id.Should().Be(ticket.Id.Value);
        dto.RestaurantId.Should().Be(ticket.RestaurantId);
        dto.CustomerId.Should().Be(ticket.CustomerId);
        dto.OrderNumber.Should().Be("ORD-2026-0001");
        dto.Status.Should().Be(KitchenTicketStatus.InProgress);
        dto.Notes.Should().Be("table by the window");
        dto.ConfirmedByUserId.Should().Be(ticket.ConfirmedByUserId);

        dto.Items.Should().ContainSingle();
        KitchenTicketItemDto item = dto.Items.Single();
        item.MenuItemName.Should().Be("Burger");
        item.Quantity.Should().Be(2);
        item.SpecialInstructions.Should().Be("no pickles");
        item.SeatNumber.Should().Be(3);
        item.Status.Should().Be(KitchenItemStatus.Pending);
    }

    [Fact]
    public async Task Handle_UnknownTicket_ThrowsNotFound()
    {
        GetTicketByIdHandler handler = BuildHandler(ticket: null);
        Guid unknownId = Guid.NewGuid();

        Func<Task> act = () => handler.Handle(
            new GetTicketByIdQuery(unknownId),
            CancellationToken.None);

        await act.Should().ThrowAsync<KitchenTicketNotFoundException>();
    }
}
