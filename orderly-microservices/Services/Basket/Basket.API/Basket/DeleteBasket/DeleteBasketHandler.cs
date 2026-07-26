namespace Basket.API.Basket.DeleteBasket;

public record DeleteBasketCommand(Guid UserId, Guid RestaurantId) : ICommand<Unit>, IBasketIdentityRequest;

public class DeleteBasketCommandValidator : AbstractValidator<DeleteBasketCommand>
{
    public DeleteBasketCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId is required.");
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required.");
    }
}

public class DeleteBasketHandler(IBasketRepository basketRepository) : ICommandHandler<DeleteBasketCommand, Unit>
{
    /// <summary>
    /// Idempotent delete — the contract is 204 No Content on
    /// both the "cart exists" and "cart already absent" paths, so the
    /// handler returns <see cref="Unit.Value"/> regardless of whether
    /// the Marten row was present. The endpoint ignores the boolean
    /// the inner repository returns (it was historically
    /// <c>DeleteBasketResult.IsSuccess</c>); the side effect — cache
    /// invalidation — happens inside <see cref="Data.CachedBasketRepository.DeleteBasketAsync"/>
    /// when the inner repository actually deletes a row.
    /// </summary>
    public async Task<Unit> Handle(DeleteBasketCommand request, CancellationToken cancellationToken)
    {
        await basketRepository.DeleteBasketAsync(request.UserId, request.RestaurantId, cancellationToken);

        return Unit.Value;
    }
}
