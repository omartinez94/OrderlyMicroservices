namespace Catalog.API.Features.IngredientAlternatives.CreateIngredientAlternative;

public record CreateIngredientAlternativeCommand(
    Guid RestaurantId,
    int OriginalIngredientId,
    int AlternativeIngredientId,
    decimal PriceModifier,
    bool AutoSubstitute) : ICommand<CreateIngredientAlternativeResult>;

public record CreateIngredientAlternativeResult(int Id);

public class CreateIngredientAlternativeCommandValidator : AbstractValidator<CreateIngredientAlternativeCommand>
{
    public CreateIngredientAlternativeCommandValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.OriginalIngredientId)
            .GreaterThan(0).WithMessage("OriginalIngredientId is required");
        RuleFor(x => x.AlternativeIngredientId)
            .GreaterThan(0).WithMessage("AlternativeIngredientId is required")
            .NotEqual(x => x.OriginalIngredientId).WithMessage("AlternativeIngredient cannot be the same as OriginalIngredient");
        RuleFor(x => x.PriceModifier)
            .NotNull().WithMessage("PriceModifier is required");
    }
}

internal class CreateIngredientAlternativeCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache) : ICommandHandler<CreateIngredientAlternativeCommand, CreateIngredientAlternativeResult>
{
    public async Task<CreateIngredientAlternativeResult> Handle(CreateIngredientAlternativeCommand command, CancellationToken cancellationToken)
    {
        var ingredientAlternative = new IngredientAlternative
        {
            RestaurantId = command.RestaurantId,
            OriginalIngredientId = command.OriginalIngredientId,
            AlternativeIngredientId = command.AlternativeIngredientId,
            PriceModifier = command.PriceModifier,
            AutoSubstitute = command.AutoSubstitute
        };

        dbContext.IngredientAlternatives.Add(ingredientAlternative);

        // Domain event BEFORE SaveChanges so the dispatcher drains it.
        ingredientAlternative.AddDomainEvent(new IngredientAlternativeChangedDomainEvent(
            ingredientAlternative.Id,
            ingredientAlternative.RestaurantId,
            ingredientAlternative.OriginalIngredientId,
            ingredientAlternative.AlternativeIngredientId,
            IngredientAlternativeChangedDomainEvent.ChangeKind.Created));

        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateIngredientsAsync(command.RestaurantId, cancellationToken);

        return new CreateIngredientAlternativeResult(ingredientAlternative.Id);
    }
}
