namespace Catalog.API.Features.Ingredients.CreateIngredient;

public record CreateIngredientCommand(
    Guid RestaurantId,
    string Name,
    string Unit,
    decimal CurrentStock,
    decimal MinimumStock,
    bool IsAvailable) : ICommand<CreateIngredientResult>;

public record CreateIngredientResult(int Id);

public class CreateIngredientCommandValidator : AbstractValidator<CreateIngredientCommand>
{
    public CreateIngredientCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(255).WithMessage("Name must not exceed 255 characters");
        RuleFor(x => x.Unit).MaximumLength(50).WithMessage("Unit must not exceed 50 characters");
        RuleFor(x => x.CurrentStock).GreaterThanOrEqualTo(0).WithMessage("CurrentStock must be greater than or equal to 0");
        RuleFor(x => x.MinimumStock).GreaterThanOrEqualTo(0).WithMessage("MinimumStock must be greater than or equal to 0");
    }
}

internal class CreateIngredientCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache) : ICommandHandler<CreateIngredientCommand, CreateIngredientResult>
{
    public async Task<CreateIngredientResult> Handle(CreateIngredientCommand command, CancellationToken cancellationToken)
    {
        var ingredient = new Ingredient
        {
            RestaurantId = command.RestaurantId,
            Name = command.Name,
            Unit = command.Unit,
            CurrentStock = command.CurrentStock,
            MinimumStock = command.MinimumStock,
            IsAvailable = command.IsAvailable
        };

        dbContext.Ingredients.Add(ingredient);
        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateIngredientsAsync(command.RestaurantId, cancellationToken);

        return new CreateIngredientResult(ingredient.Id);
    }
}
