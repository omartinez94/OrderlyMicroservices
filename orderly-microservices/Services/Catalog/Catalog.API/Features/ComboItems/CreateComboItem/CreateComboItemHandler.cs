namespace Catalog.API.Features.ComboItems.CreateComboItem;

public record CreateComboItemCommand(
    Guid ComboMenuItemId,
    Guid IncludedMenuItemId,
    int Quantity,
    bool IsOptional) : ICommand<CreateComboItemResult>;

public record CreateComboItemResult(int Id);

public class CreateComboItemCommandValidator : AbstractValidator<CreateComboItemCommand>
{
    public CreateComboItemCommandValidator()
    {
        RuleFor(x => x.ComboMenuItemId).NotEmpty().WithMessage("ComboMenuItemId is required");
        RuleFor(x => x.IncludedMenuItemId).NotEmpty().WithMessage("IncludedMenuItemId is required");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than 0");
    }
}

internal class CreateComboItemCommandHandler(CatalogDbContext dbContext) : ICommandHandler<CreateComboItemCommand, CreateComboItemResult>
{
    public async Task<CreateComboItemResult> Handle(CreateComboItemCommand command, CancellationToken cancellationToken)
    {
        var comboItem = new ComboItem
        {
            ComboMenuItemId = command.ComboMenuItemId,
            IncludedMenuItemId = command.IncludedMenuItemId,
            Quantity = command.Quantity,
            IsOptional = command.IsOptional
        };

        dbContext.ComboItems.Add(comboItem);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateComboItemResult(comboItem.Id);
    }
}
