using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Ingredients.DeleteIngredient;

public record DeleteIngredientCommand(Guid RestaurantId, int Id) : ICommand<DeleteIngredientResult>;

public record DeleteIngredientResult(bool IsSuccess);

public class DeleteIngredientCommandValidator : AbstractValidator<DeleteIngredientCommand>
{
    public DeleteIngredientCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id is required");
    }
}

internal class DeleteIngredientCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache) : ICommandHandler<DeleteIngredientCommand, DeleteIngredientResult>
{
    public async Task<DeleteIngredientResult> Handle(DeleteIngredientCommand command, CancellationToken cancellationToken)
    {
        var ingredient = await dbContext.Ingredients.FirstOrDefaultAsync(i => i.RestaurantId == command.RestaurantId && i.Id == command.Id, cancellationToken)
            ?? throw new IngredientNotFoundException(command.Id);

        dbContext.Ingredients.Remove(ingredient);
        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateIngredientsAsync(command.RestaurantId, cancellationToken);

        return new DeleteIngredientResult(true);
    }
}
