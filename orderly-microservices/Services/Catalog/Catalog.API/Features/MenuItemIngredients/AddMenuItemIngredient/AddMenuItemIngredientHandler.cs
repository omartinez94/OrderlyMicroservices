namespace Catalog.API.Features.MenuItemIngredients.AddMenuItemIngredient;

public record AddMenuItemIngredientCommand(
    Guid MenuItemId,
    int IngredientId,
    decimal QuantityRequired,
    bool IsOptional) : ICommand<AddMenuItemIngredientResult>;

public record AddMenuItemIngredientResult(int Id);

public class AddMenuItemIngredientCommandValidator : AbstractValidator<AddMenuItemIngredientCommand>
{
    public AddMenuItemIngredientCommandValidator()
    {
        RuleFor(x => x.MenuItemId).NotEmpty().WithMessage("MenuItemId is required");
        RuleFor(x => x.IngredientId).GreaterThan(0).WithMessage("IngredientId is required");
        RuleFor(x => x.QuantityRequired).GreaterThan(0).WithMessage("QuantityRequired must be greater than 0");
    }
}

internal class AddMenuItemIngredientCommandHandler(CatalogDbContext dbContext) : ICommandHandler<AddMenuItemIngredientCommand, AddMenuItemIngredientResult>
{
    public async Task<AddMenuItemIngredientResult> Handle(AddMenuItemIngredientCommand command, CancellationToken cancellationToken)
    {
        var ingredient = new MenuItemIngredient
        {
            MenuItemId = command.MenuItemId,
            IngredientId = command.IngredientId,
            QuantityRequired = command.QuantityRequired,
            IsOptional = command.IsOptional
        };

        dbContext.MenuItemIngredients.Add(ingredient);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AddMenuItemIngredientResult(ingredient.Id);
    }
}
