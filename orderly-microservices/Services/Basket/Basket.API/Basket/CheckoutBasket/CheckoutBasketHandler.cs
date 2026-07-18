using BuildingBlocks.Messaging.Events;
using MassTransit;

namespace Basket.API.Basket.CheckoutBasket;

[PciSensitive]
public record CheckoutBasketCommand(BasketCheckoutDto BasketCheckoutDto) : ICommand<CheckoutBasketResult>, IBasketIdentityRequest
{
    public Guid UserId => BasketCheckoutDto.UserId;
    public Guid RestaurantId => BasketCheckoutDto.RestaurantId;
}

public record CheckoutBasketResult(bool Success, string Message);

public class CheckoutBasketCommandValidator : AbstractValidator<CheckoutBasketCommand>
{
    public CheckoutBasketCommandValidator()
    {
        RuleFor(x => x.BasketCheckoutDto.UserId).NotEmpty().WithMessage("UserId is required.");
        RuleFor(x => x.BasketCheckoutDto.RestaurantId).NotEmpty().WithMessage("RestaurantId is required.");
    }
}

public class CheckoutBasketCommandHandler(
    IBasketRepository basketRepository,
    IPublishEndpoint publishEndpoint)
    : ICommandHandler<CheckoutBasketCommand, CheckoutBasketResult>
{
    public async Task<CheckoutBasketResult> Handle(CheckoutBasketCommand command, CancellationToken cancellationToken)
    {
        var basket = await basketRepository.GetBasketAsync(command.BasketCheckoutDto.UserId, command.BasketCheckoutDto.RestaurantId, cancellationToken);

        if (basket.Items.Count == 0)
        {
            return new CheckoutBasketResult(false, "Basket is empty.");
        }

        var eventmessage = command.BasketCheckoutDto.Adapt<BasketCheckoutEvent>() with
        {
            TotalAmount = basket.Subtotal,
            AppliedDiscounts = basket.AppliedDiscounts,
            Items = [.. basket.Items.Select(item => new BasketCheckoutItem
            {
                MenuItemId = item.MenuItemId,
                Name = string.Empty,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                TotalPrice = item.TotalPrice,
                Variations = [.. item.Variations.Select(v => new BasketItemVariationDto
                {
                    Name = v.Name,
                    Value = v.Value,
                    Price = v.Price
                })],
                Customizations = [.. item.Customizations.Select(c => new BasketItemCustomizationDto
                {
                    Ingredient = c.Ingredient,
                    Action = c.Action
                })]
            })]
        };

        await publishEndpoint.Publish(eventmessage, cancellationToken);

        await basketRepository.DeleteBasketAsync(command.BasketCheckoutDto.UserId, command.BasketCheckoutDto.RestaurantId, cancellationToken);

        return new CheckoutBasketResult(true, "Checkout completed successfully.");
    }
}