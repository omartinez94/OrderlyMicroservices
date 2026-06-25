namespace Catalog.API.Features.MenuItems.DeleteMenuItem;

public record DeleteMenuItemCommand(Guid Id) : ICommand<DeleteMenuItemResult>;

public record DeleteMenuItemResult(bool Success);

public class DeleteMenuItemCommandValidator : AbstractValidator<DeleteMenuItemCommand>
{
    public DeleteMenuItemCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id is required");
    }
}

internal class DeleteMenuItemCommandHandler(CatalogDbContext dbContext) : ICommandHandler<DeleteMenuItemCommand, DeleteMenuItemResult>
{
    public async Task<DeleteMenuItemResult> Handle(DeleteMenuItemCommand command, CancellationToken cancellationToken)
    {
        var menuItem = await dbContext.MenuItems
            .FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken);

        if (menuItem is null)
        {
            throw new NotFoundException(nameof(MenuItem), command.Id);
        }

        menuItem.IsDeleted = true;
        menuItem.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        menuItem.IsAvailable = false;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteMenuItemResult(true);
    }
}
