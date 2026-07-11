using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Ingredients.UpdateIngredient;

public record UpdateIngredientCommand(
    Guid RestaurantId,
    int Id,
    string Name,
    string Unit,
    decimal CurrentStock,
    decimal MinimumStock,
    bool IsAvailable) : ICommand<UpdateIngredientResult>;

public record UpdateIngredientResult(bool IsSuccess);

public class UpdateIngredientCommandValidator : AbstractValidator<UpdateIngredientCommand>
{
    public UpdateIngredientCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id is required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(255).WithMessage("Name must not exceed 255 characters");
        RuleFor(x => x.Unit).MaximumLength(50).WithMessage("Unit must not exceed 50 characters");
        RuleFor(x => x.CurrentStock).GreaterThanOrEqualTo(0).WithMessage("CurrentStock must be greater than or equal to 0");
        RuleFor(x => x.MinimumStock).GreaterThanOrEqualTo(0).WithMessage("MinimumStock must be greater than or equal to 0");
    }
}

internal class UpdateIngredientCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache) : ICommandHandler<UpdateIngredientCommand, UpdateIngredientResult>
{
    public async Task<UpdateIngredientResult> Handle(UpdateIngredientCommand command, CancellationToken cancellationToken)
    {
        var ingredient = await dbContext.Ingredients.FirstOrDefaultAsync(i => i.RestaurantId == command.RestaurantId && i.Id == command.Id, cancellationToken)
            ?? throw new IngredientNotFoundException(command.Id);

        ingredient.Name = command.Name;
        ingredient.Unit = command.Unit;
        ingredient.CurrentStock = command.CurrentStock;
        ingredient.MinimumStock = command.MinimumStock;
        ingredient.IsAvailable = command.IsAvailable;

        dbContext.Ingredients.Update(ingredient);

        // Raise the domain event BEFORE SaveChanges so the
        // DispatchDomainEventsInterceptor drains it (pre-commit).
        ingredient.AddDomainEvent(new IngredientChangedDomainEvent(
            ingredient.Id,
            ingredient.RestaurantId,
            IngredientChangedDomainEvent.ChangeKind.Updated));

        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateIngredientsAsync(command.RestaurantId, cancellationToken);

        return new UpdateIngredientResult(true);
    }
}
