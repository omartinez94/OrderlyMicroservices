using BuildingBlocks.Messaging.Events.Catalog;
using Kitchen.API.Application.EventHandlers.Integration;
using Kitchen.API.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Kitchen.API.Tests.Integration;

public sealed class MenuItemChangedIntegrationEventHandlerTests
{
    [Fact]
    public async Task Consume_BroadcastsMenuItemChangedDto_ToRestaurantGroup()
    {
        IHubContext<KitchenHub, IKitchenHubClient> hubContext = Substitute.For<IHubContext<KitchenHub, IKitchenHubClient>>();
        IHubClients<IKitchenHubClient> hubClients = Substitute.For<IHubClients<IKitchenHubClient>>();
        IKitchenHubClient groupProxy = Substitute.For<IKitchenHubClient>();

        hubContext.Clients.Returns(hubClients);
        hubClients.Group(Arg.Any<string>()).Returns(groupProxy);

        MenuItemChangedIntegrationEventHandler handler = new(hubContext, NullLogger<MenuItemChangedIntegrationEventHandler>.Instance);

        Guid itemId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        MenuItemChangedIntegrationEvent message = new()
        {
            MenuItemId = itemId,
            RestaurantId = restaurantId,
            ChangeType = MenuItemChangeType.Updated,
            Name = "Spaghetti Carbonara",
            BasePrice = 16.50m,
            IsAvailable = true,
            AvailabilityStatus = "Available",
            ImageUrl = "http://blob.local/carbonara.png",
        };

        ConsumeContext<MenuItemChangedIntegrationEvent> context = Substitute.For<ConsumeContext<MenuItemChangedIntegrationEvent>>();
        context.Message.Returns(message);

        await handler.Consume(context);

        hubClients.Received(1).Group($"restaurant:{restaurantId}");
        await groupProxy.Received(1).MenuItemChanged(Arg.Is<MenuItemChangedDto>(dto =>
            dto.MenuItemId == itemId &&
            dto.RestaurantId == restaurantId &&
            dto.ChangeType == "Updated" &&
            dto.Name == "Spaghetti Carbonara" &&
            dto.BasePrice == 16.50m &&
            dto.IsAvailable == true &&
            dto.AvailabilityStatus == "Available" &&
            dto.ImageUrl == "http://blob.local/carbonara.png"));
    }
}
