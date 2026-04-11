namespace Basket.API.Basket.DeleteBasket;

public record DeleteBasketCommand(Guid UserId, Guid RestaurantId) : ICommand<DeleteBasketResult>;
public record DeleteBasketResult(bool IsSuccess);

public class DeleteBasketCommandValidator : AbstractValidator<DeleteBasketCommand>
{
    public DeleteBasketCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId is required.");
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required.");
    }
}

public class DeleteBasketHandler(IBasketRepository basketRepository) : ICommandHandler<DeleteBasketCommand, DeleteBasketResult>
{
    public async Task<DeleteBasketResult> Handle(DeleteBasketCommand request, CancellationToken cancellationToken)
    {
        var isSuccess = await basketRepository.DeleteBasketAsync(request.UserId, request.RestaurantId, cancellationToken);

        return new DeleteBasketResult(isSuccess);
    }
}
