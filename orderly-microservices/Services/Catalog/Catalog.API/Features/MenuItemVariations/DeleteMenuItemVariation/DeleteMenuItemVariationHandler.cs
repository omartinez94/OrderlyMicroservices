using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItemVariations.DeleteMenuItemVariation;

public record DeleteMenuItemVariationCommand(int Id) : ICommand<DeleteMenuItemVariationResult>;

public record DeleteMenuItemVariationResult(bool Success);

public class DeleteMenuItemVariationCommandValidator : AbstractValidator<DeleteMenuItemVariationCommand>
{
    public DeleteMenuItemVariationCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
    }
}

internal class DeleteMenuItemVariationCommandHandler(CatalogDbContext dbContext) : ICommandHandler<DeleteMenuItemVariationCommand, DeleteMenuItemVariationResult>
{
    public async Task<DeleteMenuItemVariationResult> Handle(DeleteMenuItemVariationCommand command, CancellationToken cancellationToken)
    {
        var variation = await dbContext.MenuItemVariations
            .FirstOrDefaultAsync(v => v.Id == command.Id && !v.IsDeleted, cancellationToken);

        if (variation is null)
        {
            throw new NotFoundException("MenuItemVariation", command.Id);
        }

        variation.IsDeleted = true;
        variation.DeletedAt = SystemClock.Instance.GetCurrentInstant();

        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteMenuItemVariationResult(true);
    }
}
