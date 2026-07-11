using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.IngredientAlternatives.DeleteIngredientAlternative;

public record DeleteIngredientAlternativeCommand(int Id, Guid RestaurantId) : ICommand<DeleteIngredientAlternativeResult>;

public record DeleteIngredientAlternativeResult(bool Success);

public class DeleteIngredientAlternativeCommandValidator : AbstractValidator<DeleteIngredientAlternativeCommand>
{
    public DeleteIngredientAlternativeCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id is required");
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
    }
}

internal class DeleteIngredientAlternativeCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache) : ICommandHandler<DeleteIngredientAlternativeCommand, DeleteIngredientAlternativeResult>
{
    public async Task<DeleteIngredientAlternativeResult> Handle(DeleteIngredientAlternativeCommand command, CancellationToken cancellationToken)
    {
        var ingredientAlternative = await dbContext.IngredientAlternatives
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.RestaurantId == command.RestaurantId, cancellationToken);

        if (ingredientAlternative is null)
        {
            throw new NotFoundException(nameof(IngredientAlternative), command.Id);
        }

        dbContext.IngredientAlternatives.Remove(ingredientAlternative);
        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateIngredientsAsync(command.RestaurantId, cancellationToken);

        return new DeleteIngredientAlternativeResult(true);
    }
}
