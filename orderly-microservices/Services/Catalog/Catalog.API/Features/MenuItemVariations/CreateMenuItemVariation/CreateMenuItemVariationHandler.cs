using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItemVariations.CreateMenuItemVariation;

public record CreateMenuItemVariationCommand(
    Guid MenuItemId,
    string Name,
    string VariationValue,
    decimal PriceModifier,
    bool IsDefault,
    int DisplayOrder) : ICommand<CreateMenuItemVariationResult>;

public record CreateMenuItemVariationResult(int Id);

public class CreateMenuItemVariationCommandValidator : AbstractValidator<CreateMenuItemVariationCommand>
{
    public CreateMenuItemVariationCommandValidator()
    {
        RuleFor(x => x.MenuItemId).NotEmpty().WithMessage("MenuItemId is required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(100);
        RuleFor(x => x.VariationValue).NotEmpty().WithMessage("VariationValue is required").MaximumLength(100);
    }
}

internal class CreateMenuItemVariationCommandHandler(CatalogDbContext dbContext) : ICommandHandler<CreateMenuItemVariationCommand, CreateMenuItemVariationResult>
{
    public async Task<CreateMenuItemVariationResult> Handle(CreateMenuItemVariationCommand command, CancellationToken cancellationToken)
    {
        var menuItemExists = await dbContext.MenuItems.AnyAsync(m => m.Id == command.MenuItemId && !m.IsDeleted, cancellationToken);
        if (!menuItemExists)
        {
            throw new NotFoundException("MenuItem", command.MenuItemId);
        }

        var variation = new MenuItemVariation
        {
            Id = 0,
            MenuItemId = command.MenuItemId,
            Name = command.Name,
            VariationValue = command.VariationValue,
            PriceModifier = command.PriceModifier,
            IsDefault = command.IsDefault,
            DisplayOrder = command.DisplayOrder,
            IsDeleted = false
        };

        dbContext.MenuItemVariations.Add(variation);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateMenuItemVariationResult(variation.Id);
    }
}
