using Discount.Grpc;

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

public class StoreBasketHandler
    (IBasketRepository basketRepository, DiscountProtoService.DiscountProtoServiceClient discountService) 
    : ICommandHandler<StoreBasketCommand, StoreBasketResult>
{
    public async Task<StoreBasketResult> Handle(StoreBasketCommand command, CancellationToken cancellationToken)
    {
        foreach (var couponCode in command.Basket.AppliedDiscounts)
        {
            var discountResponse = await discountService.GetDiscountAsync(new GetDiscountRequest
            {
                RestaurantId = command.Basket.RestaurantId.ToString(),
                Code = couponCode
            }, cancellationToken: cancellationToken);

            if (discountResponse.Coupon.IsActive == false)
            {
                continue;
            }

            var discountAmount = discountResponse.Coupon.Amount;

            if (discountAmount > 0 && command.Basket.Items.Count != 0)
            {
                #warning TODO: Implement the discount deduction logic. This is a simplified example and may need to be adjusted based on how discounts are structured (e.g., percentage vs fixed amount).

            }
        }

        var basket = await basketRepository.StoreBasketAsync(command.Basket, cancellationToken);

        return new StoreBasketResult(basket.UserId, basket.RestaurantId);
    }
}
