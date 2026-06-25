namespace Catalog.API.Features.MenuItemVariations.UpdateMenuItemVariation;

public record UpdateMenuItemVariationCommand(
    int Id,
    string Name,
    string VariationValue,
    decimal PriceModifier,
    bool IsDefault,
    int DisplayOrder) : ICommand<UpdateMenuItemVariationResult>;

public record UpdateMenuItemVariationResult(bool Success);

public class UpdateMenuItemVariationCommandValidator : AbstractValidator<UpdateMenuItemVariationCommand>
{
    public UpdateMenuItemVariationCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(100);
        RuleFor(x => x.VariationValue).NotEmpty().WithMessage("VariationValue is required").MaximumLength(100);
    }
}

internal class UpdateMenuItemVariationCommandHandler(CatalogDbContext dbContext) : ICommandHandler<UpdateMenuItemVariationCommand, UpdateMenuItemVariationResult>
{
    public async Task<UpdateMenuItemVariationResult> Handle(UpdateMenuItemVariationCommand command, CancellationToken cancellationToken)
    {
        var variation = await dbContext.MenuItemVariations
            .FirstOrDefaultAsync(v => v.Id == command.Id && !v.IsDeleted, cancellationToken);

        if (variation is null)
        {
            throw new NotFoundException("MenuItemVariation", command.Id);
        }

        variation.Name = command.Name;
        variation.VariationValue = command.VariationValue;
        variation.PriceModifier = command.PriceModifier;
        variation.IsDefault = command.IsDefault;
        variation.DisplayOrder = command.DisplayOrder;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateMenuItemVariationResult(true);
    }
}
