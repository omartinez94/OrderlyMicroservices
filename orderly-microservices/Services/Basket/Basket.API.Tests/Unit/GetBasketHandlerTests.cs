using Basket.API.Basket.GetBasket;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Unit-level coverage for <see cref="GetBasketHandler"/>. Locks the
/// §0.4.7 contract: when no cart exists, the handler returns an empty
/// <see cref="Models.Basket"/> projected from the supplied ids — it
/// does NOT throw <see cref="BasketNotFoundException"/>.
/// </summary>
public sealed class GetBasketHandlerTests
{
    [Fact]
    public async Task NoCartYet_ReturnsEmptyBasket()
    {
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var repository = Substitute.For<IBasketRepository>();
        repository
            .GetActiveCartOrEmptyAsync(userId, restaurantId, Arg.Any<CancellationToken>())
            .Returns(new Models.Basket(userId, restaurantId));

        var handler = new GetBasketHandler(repository);

        var result = await handler.Handle(
            new GetBasketQuery(userId, restaurantId),
            CancellationToken.None);

        result.Basket.Should().NotBeNull();
        result.Basket.UserId.Should().Be(userId);
        result.Basket.RestaurantId.Should().Be(restaurantId);
        result.Basket.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ExistingCart_ReturnsCart()
    {
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var existingBasket = new Models.Basket(userId, restaurantId)
        {
            Items = { new Models.BasketItem { MenuItemId = 42, Quantity = 2, UnitPrice = 9.99m } },
        };
        var repository = Substitute.For<IBasketRepository>();
        repository
            .GetActiveCartOrEmptyAsync(userId, restaurantId, Arg.Any<CancellationToken>())
            .Returns(existingBasket);

        var handler = new GetBasketHandler(repository);

        var result = await handler.Handle(
            new GetBasketQuery(userId, restaurantId),
            CancellationToken.None);

        result.Basket.Should().BeSameAs(existingBasket);
        result.Basket.Items.Should().HaveCount(1);
    }
}