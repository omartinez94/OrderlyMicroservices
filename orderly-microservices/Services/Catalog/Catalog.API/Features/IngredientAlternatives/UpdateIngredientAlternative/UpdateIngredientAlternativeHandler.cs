using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.IngredientAlternatives.UpdateIngredientAlternative;

public record UpdateIngredientAlternativeCommand(
    int Id,
    Guid RestaurantId,
    int OriginalIngredientId,
    int AlternativeIngredientId,
    decimal PriceModifier,
    bool AutoSubstitute) : ICommand<UpdateIngredientAlternativeResult>;

public record UpdateIngredientAlternativeResult(bool Success);

public class UpdateIngredientAlternativeCommandValidator : AbstractValidator<UpdateIngredientAlternativeCommand>
{
    public UpdateIngredientAlternativeCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id is required");
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.OriginalIngredientId).GreaterThan(0).WithMessage("OriginalIngredientId is required");
        RuleFor(x => x.AlternativeIngredientId).GreaterThan(0).WithMessage("AlternativeIngredientId is required")
            .NotEqual(x => x.OriginalIngredientId).WithMessage("AlternativeIngredient cannot be the same as OriginalIngredient");
        RuleFor(x => x.PriceModifier).NotNull().WithMessage("PriceModifier is required");
    }
}

internal class UpdateIngredientAlternativeCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache,
    IPriceHistoryRecorder priceHistory,
    ICurrentUser currentUser) : ICommandHandler<UpdateIngredientAlternativeCommand, UpdateIngredientAlternativeResult>
{
    public async Task<UpdateIngredientAlternativeResult> Handle(UpdateIngredientAlternativeCommand command, CancellationToken cancellationToken)
    {
        var ingredientAlternative = await dbContext.IngredientAlternatives
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.RestaurantId == command.RestaurantId, cancellationToken);

        if (ingredientAlternative is null)
        {
            throw new NotFoundException(nameof(IngredientAlternative), command.Id);
        }

        var oldPriceModifier = ingredientAlternative.PriceModifier;

        ingredientAlternative.OriginalIngredientId = command.OriginalIngredientId;
        ingredientAlternative.AlternativeIngredientId = command.AlternativeIngredientId;
        ingredientAlternative.PriceModifier = command.PriceModifier;
        ingredientAlternative.AutoSubstitute = command.AutoSubstitute;

        dbContext.IngredientAlternatives.Update(ingredientAlternative);

        // Phase 4: append a PriceHistory row when the alternative's price
        // modifier changed. The recorder skips no-op writes internally.
        priceHistory.Record(
            restaurantId: ingredientAlternative.RestaurantId,
            priceType: PriceType.IngredientAlternative,
            oldPrice: oldPriceModifier,
            newPrice: command.PriceModifier,
            reason: $"IngredientAlternative {ingredientAlternative.Id} price modifier update",
            changedByUserId: currentUser.UserId,
            ingredientAlternativeId: ingredientAlternative.Id,
            ct: cancellationToken);

        // Domain event BEFORE SaveChanges.
        ingredientAlternative.AddDomainEvent(new IngredientAlternativeChangedDomainEvent(
            ingredientAlternative.Id,
            ingredientAlternative.RestaurantId,
            ingredientAlternative.OriginalIngredientId,
            ingredientAlternative.AlternativeIngredientId,
            IngredientAlternativeChangedDomainEvent.ChangeKind.Updated));

        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateIngredientsAsync(command.RestaurantId, cancellationToken);

        return new UpdateIngredientAlternativeResult(true);
    }
}
