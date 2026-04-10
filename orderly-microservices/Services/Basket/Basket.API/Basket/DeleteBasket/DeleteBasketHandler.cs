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

public class DeleteBasketHandler(IDocumentSession session) : ICommandHandler<DeleteBasketCommand, DeleteBasketResult>
{
    public async Task<DeleteBasketResult> Handle(DeleteBasketCommand request, CancellationToken cancellationToken)
    {
        var baskets = await session.Query<Models::Basket>()
            .Where(b => b.UserId == request.UserId && b.RestaurantId == request.RestaurantId)
            .ToListAsync(cancellationToken);

        foreach (var basket in baskets)
        {
            session.Delete(basket);
        }

        await session.SaveChangesAsync(cancellationToken);

        return new DeleteBasketResult(true);
    }
}
