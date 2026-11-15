using Kitchen.API.Application.Dtos;
using Kitchen.API.Application.Extensions;

namespace Kitchen.API.Tests.Extensions;

public sealed class KitchenTicketExtensionsTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 7, 4, 12, 0);

    [Fact]
    public void ToDto_MapsSeatNumber_WhenItemHasSeatNumber()
    {
        OrderItemSeed seedWithSeat = new OrderItemSeed(
            OrderItemId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid(),
            MenuItemName: "Soup",
            Quantity: 1,
            UnitPrice: 5.50m,
            SelectedVariations: [],
            Customizations: [],
            SpecialInstructions: null,
            SeatNumber: 2);

        OrderItemSeed seedWithoutSeat = new OrderItemSeed(
            OrderItemId: Guid.NewGuid(),
            MenuItemId: Guid.NewGuid(),
            MenuItemName: "Steak",
            Quantity: 1,
            UnitPrice: 25.00m,
            SelectedVariations: [],
            Customizations: [],
            SpecialInstructions: null,
            SeatNumber: null);

        KitchenTicket ticket = KitchenTicket.CreateFromOrder(
            orderId: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            orderNumber: "ORD-2026-0003",
            itemSeeds: [seedWithSeat, seedWithoutSeat],
            notes: "Quick prep",
            receivedAt: Now);

        KitchenTicketDto dto = ticket.ToDto();

        dto.Items.Should().HaveCount(2);

        KitchenTicketItemDto item1 = dto.Items.First(i => i.OrderItemId == seedWithSeat.OrderItemId);
        item1.SeatNumber.Should().Be(2);

        KitchenTicketItemDto item2 = dto.Items.First(i => i.OrderItemId == seedWithoutSeat.OrderItemId);
        item2.SeatNumber.Should().BeNull();
    }

    [Fact]
    public void ToOrderItemSeeds_MapsSeatNumber_FromIntegrationEvent()
    {
        OrderCreatedIntegrationEvent evt = new OrderCreatedIntegrationEvent
        {
            OrderId = Guid.NewGuid(),
            OrderNumber = "ORD-2026-0004",
            RestaurantId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Subtotal = 30m,
            TotalAmount = 30m,
            TaxAmount = 0m,
            Currency = "USD",
            Items =
            [
                new KitchenOrderItemPreview(
                    OrderItemId: Guid.NewGuid(),
                    MenuItemId: Guid.NewGuid(),
                    MenuItemName: "Salad",
                    Quantity: 1,
                    UnitPrice: 8m,
                    SelectedVariations: [],
                    Customizations: [],
                    SpecialInstructions: null,
                    SeatNumber: 1),
                new KitchenOrderItemPreview(
                    OrderItemId: Guid.NewGuid(),
                    MenuItemId: Guid.NewGuid(),
                    MenuItemName: "Fish",
                    Quantity: 1,
                    UnitPrice: 22m,
                    SelectedVariations: [],
                    Customizations: [],
                    SpecialInstructions: null,
                    SeatNumber: 2),
                new KitchenOrderItemPreview(
                    OrderItemId: Guid.NewGuid(),
                    MenuItemId: Guid.NewGuid(),
                    MenuItemName: "Water",
                    Quantity: 1,
                    UnitPrice: 0m,
                    SelectedVariations: [],
                    Customizations: [],
                    SpecialInstructions: null,
                    SeatNumber: null)
            ]
        };

        IReadOnlyList<OrderItemSeed> seeds = evt.ToOrderItemSeeds();

        seeds.Should().HaveCount(3);
        seeds[0].SeatNumber.Should().Be(1);
        seeds[1].SeatNumber.Should().Be(2);
        seeds[2].SeatNumber.Should().BeNull();
    }
}
