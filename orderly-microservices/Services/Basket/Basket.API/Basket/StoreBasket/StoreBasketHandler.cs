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

public class StoreBasketHandler(IDocumentSession session) : ICommandHandler<StoreBasketCommand, StoreBasketResult>
{
    public async Task<StoreBasketResult> Handle(StoreBasketCommand command, CancellationToken cancellationToken)
    {
        var existingBasket = await session.Query<Models::Basket>()
            .Where(b => b.UserId == command.Basket.UserId && b.RestaurantId == command.Basket.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);
            
        if (existingBasket is not null)
        {
            // Using ID assignment if Basket had one, but we assume entity replacement here.
            session.Delete(existingBasket);
        }

        session.Store(command.Basket);
        await session.SaveChangesAsync(cancellationToken);

        return new StoreBasketResult(command.Basket.UserId, command.Basket.RestaurantId);
    }
}
