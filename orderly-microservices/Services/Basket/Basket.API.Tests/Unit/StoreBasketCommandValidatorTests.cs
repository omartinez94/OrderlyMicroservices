using Basket.API.Basket.StoreBasket;
using ModelsBasket = Basket.API.Models.Basket;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="StoreBasketCommandValidator"/>. Locks the
/// Contract for the basket body shape: per-item
/// <c>MenuItemId / Quantity / UnitPrice / Variations / Customizations</c>
/// constraints, item count, distinct coupon codes, and the
/// regex shape of each coupon code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deviation from the original unit tests.</b> The
/// previous test class included two tests that
/// expected the validator to reject non-empty body
/// <c>UserId</c> / <c>RestaurantId</c> —
/// "spoofing-footgun" check. That rule was REMOVED
/// because it was broken: the endpoint overwrites
/// <c>Basket.UserId</c> / <c>Basket.RestaurantId</c> from the
/// JWT BEFORE constructing the command, so the validator was
/// always seeing the JWT values (non-empty), not the body's
/// pre-overwrite values. The protection now lives in (a) the
/// endpoint overwrite itself (the caller cannot inject a
/// different identity via the body) + (b) the second-layer
/// <c>BasketIdentityGuardBehavior</c> cross-check. A
/// follow-up re-introduces the body-shape spoofing
/// check as an <c>IEndpointFilter</c> that runs BEFORE the
/// endpoint code (so it sees the body's pre-overwrite values),
/// at which point this test class adds a
/// <c>BodyWithNonEmptyUserId_FailsValidation</c> regression.
/// </para>
/// </remarks>
public sealed class StoreBasketCommandValidatorTests
{
    [Fact]
    public void ValidBasket_PassesValidation()
    {
        var validator = new StoreBasketCommandValidator();
        var basket = new ModelsBasket(Guid.NewGuid(), Guid.NewGuid())
        {
            // Endpoint overwrites these to the JWT values; the
            // validator now sees non-empty Guid (the post-overwrite
            // state).
            Items =
            {
                new Models.BasketItem
                {
                    MenuItemId = 1,
                    Quantity = 2,
                    UnitPrice = 9.99m,
                },
            },
        };
        var command = new StoreBasketCommand(basket);

        var result = validator.Validate(command);

        result.IsValid.Should().BeTrue("the §0.4.10 item-level rules all pass for a typical cart");
    }

    [Fact]
    public void ItemQuantity_Zero_FailsValidation()
    {
        var validator = new StoreBasketCommandValidator();
        var basket = new ModelsBasket(Guid.NewGuid(), Guid.NewGuid())
        {
            Items =
            {
                new Models.BasketItem
                {
                    MenuItemId = 1,
                    Quantity = 0, // Quantity >= 1
                    UnitPrice = 1.00m,
                },
            },
        };
        var command = new StoreBasketCommand(basket);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse("A Quantity < 1 is rejected with 400");
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Quantity"));
    }

    [Fact]
    public void ItemQuantity_Over99_FailsValidation()
    {
        var validator = new StoreBasketCommandValidator();
        var basket = new ModelsBasket(Guid.NewGuid(), Guid.NewGuid())
        {
            Items =
            {
                new Models.BasketItem
                {
                    MenuItemId = 1,
                    Quantity = 100, // Quantity <= 99
                    UnitPrice = 1.00m,
                },
            },
        };
        var command = new StoreBasketCommand(basket);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse("A Quantity > 99 is rejected with 400");
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Quantity"));
    }

    [Fact]
    public void ItemUnitPrice_Zero_FailsValidation()
    {
        var validator = new StoreBasketCommandValidator();
        var basket = new ModelsBasket(Guid.NewGuid(), Guid.NewGuid())
        {
            Items =
            {
                new Models.BasketItem
                {
                    MenuItemId = 1,
                    Quantity = 1,
                    UnitPrice = 0m, // UnitPrice > 0
                },
            },
        };
        var command = new StoreBasketCommand(basket);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse("A UnitPrice of 0 is rejected with 400");
        result.Errors.Should().Contain(e => e.PropertyName.Contains("UnitPrice"));
    }

    [Fact]
    public void TooManyItems_FailsValidation()
    {
        var validator = new StoreBasketCommandValidator();
        var items = Enumerable.Range(1, 101).Select(i => new Models.BasketItem
        {
            MenuItemId = i,
            Quantity = 1,
            UnitPrice = 1m,
        }).ToList();
        var basket = new ModelsBasket(Guid.NewGuid(), Guid.NewGuid()) { Items = items };
        var command = new StoreBasketCommand(basket);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse("Items.Count > 100 is rejected with 400");
    }

    [Fact]
    public void DuplicateCouponCodes_FailsValidation()
    {
        var validator = new StoreBasketCommandValidator();
        var basket = new ModelsBasket(Guid.NewGuid(), Guid.NewGuid())
        {
            Items =
            {
                new Models.BasketItem { MenuItemId = 1, Quantity = 1, UnitPrice = 1m },
            },
            AppliedDiscounts = ["DUPLICATE", "DUPLICATE"],
        };
        var command = new StoreBasketCommand(basket);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse("Duplicate AppliedDiscounts codes are rejected");
    }

    [Fact]
    public void CouponCode_Lowercase_FailsValidation()
    {
        var validator = new StoreBasketCommandValidator();
        var basket = new ModelsBasket(Guid.NewGuid(), Guid.NewGuid())
        {
            Items =
            {
                new Models.BasketItem { MenuItemId = 1, Quantity = 1, UnitPrice = 1m },
            },
            AppliedDiscounts = ["lowercase"],
        };
        var command = new StoreBasketCommand(basket);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse("AppliedDiscounts codes must match ^[A-Z0-9_-]{4,32}$ (uppercase + digits + _-)");
    }
}
