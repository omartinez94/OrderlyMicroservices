using Microsoft.EntityFrameworkCore;

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

internal class CreateComboItemCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache,
    IOutboxPublisher outbox,
    IFeatureManager featureManager) : ICommandHandler<CreateComboItemCommand, CreateComboItemResult>
{
    public async Task<CreateComboItemResult> Handle(CreateComboItemCommand command, CancellationToken cancellationToken)
    {
        var restaurantId = await dbContext.MenuItems
            .Where(m => m.Id == command.ComboMenuItemId && !m.IsDeleted)
            .Select(m => (Guid?)m.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);
        if (restaurantId is null)
        {
            throw new NotFoundException("MenuItem", command.ComboMenuItemId);
        }

        var comboItem = new ComboItem
        {
            ComboMenuItemId = command.ComboMenuItemId,
            IncludedMenuItemId = command.IncludedMenuItemId,
            Quantity = command.Quantity,
            IsOptional = command.IsOptional
        };

        dbContext.ComboItems.Add(comboItem);
        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateMenuAsync(restaurantId.Value, cancellationToken);

        if (await featureManager.IsEnabledAsync("CatalogMenuEvents", cancellationToken).ConfigureAwait(false))
        {
            await outbox.PublishAsync(new MenuItemChangedIntegrationEvent
            {
                MenuItemId = command.ComboMenuItemId,
                RestaurantId = restaurantId.Value,
                ChangeType = MenuItemChangeType.Updated,
            }, cancellationToken).ConfigureAwait(false);
        }

        return new CreateComboItemResult(comboItem.Id);
    }
}
