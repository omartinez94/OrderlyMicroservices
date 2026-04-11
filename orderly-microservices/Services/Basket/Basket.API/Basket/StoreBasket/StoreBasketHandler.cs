namespace Basket.API.Basket.StoreBasket;

public record StoreBasketCommand(Models::Basket Basket) : ICommand<StoreBasketResult>;
public record StoreBasketResult(Guid UserId, Guid RestaurantId);

public class StoreBasketCommandValidator : AbstractValidator<StoreBasketCommand>
{
    public StoreBasketCommandValidator()
    {
        RuleFor(x => x.Basket).NotNull().WithMessage("Basket is required.");
        RuleFor(x => x.Basket.UserId).NotEmpty().WithMessage("UserId is required.");
        RuleFor(x => x.Basket.RestaurantId).NotEmpty().WithMessage("RestaurantId is required.");
    }
}

public class StoreBasketHandler(IBasketRepository basketRepository) : ICommandHandler<StoreBasketCommand, StoreBasketResult>
{
    public async Task<StoreBasketResult> Handle(StoreBasketCommand command, CancellationToken cancellationToken)
    {
        var basket = await basketRepository.StoreBasketAsync(command.Basket, cancellationToken);

        return new StoreBasketResult(basket.UserId, basket.RestaurantId);
    }
}
