using Basket.API.Basket.StoreBasket;
using ModelsBasket = Basket.API.Models.Basket;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="StoreBasketCommandValidator"/>. Locks the
/// §0.4.10 spoofing-footgun fix: the body's <c>UserId</c> and
/// <c>RestaurantId</c> MUST be <see cref="Guid.Empty"/>; any
/// user-supplied value is rejected with 422 by CustomExceptionHandler.
/// </summary>
public sealed class StoreBasketCommandValidatorTests
{
    [Fact]
    public void BodyWithEmptyUserIdAndRestaurantId_PassesValidation()
    {
        var validator = new StoreBasketCommandValidator();
        var basket = new ModelsBasket(Guid.Empty, Guid.Empty)
        {
            Items = { new Models.BasketItem { MenuItemId = 1, Quantity = 1, UnitPrice = 10m } },
        };
        var command = new StoreBasketCommand(basket);

        var result = validator.Validate(command);

        result.IsValid.Should().BeTrue("Guid.Empty is the only accepted value for UserId / RestaurantId in the body — the JWT-derived identity overwrites these before the handler runs.");
    }

    [Fact]
    public void BodyWithNonEmptyUserId_FailsValidation_With422()
    {
        var validator = new StoreBasketCommandValidator();
        var attacker = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var basket = new ModelsBasket(attacker, Guid.Empty) // attacker tries to spoof someone else's userId
        {
            Items = { new Models.BasketItem { MenuItemId = 1, Quantity = 1, UnitPrice = 10m } },
        };
        var command = new StoreBasketCommand(basket);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse("a non-empty UserId is the §0.4.10 spoofing footgun — the body MUST carry Guid.Empty");
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Basket.UserId");
        result.Errors[0].ErrorMessage.Should().Contain("JWT-derived identity is authoritative");
    }

    [Fact]
    public void BodyWithNonEmptyRestaurantId_FailsValidation_With422()
    {
        var validator = new StoreBasketCommandValidator();
        var otherRestaurant = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var basket = new ModelsBasket(Guid.Empty, otherRestaurant) // attacker tries to write into someone else's restaurant
        {
            Items = { new Models.BasketItem { MenuItemId = 1, Quantity = 1, UnitPrice = 10m } },
        };
        var command = new StoreBasketCommand(basket);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Basket.RestaurantId");
        result.Errors[0].ErrorMessage.Should().Contain("JWT-derived restaurant is authoritative");
    }
}
