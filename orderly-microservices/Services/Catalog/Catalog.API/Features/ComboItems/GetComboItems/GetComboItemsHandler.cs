using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.ComboItems.GetComboItems;

public record GetComboItemsQuery(Guid ComboMenuItemId) : IQuery<GetComboItemsResult>;

public record GetComboItemsResult(IEnumerable<ComboItemDto> ComboItems);

public class GetComboItemsQueryValidator : AbstractValidator<GetComboItemsQuery>
{
    public GetComboItemsQueryValidator()
    {
        RuleFor(x => x.ComboMenuItemId).NotEmpty().WithMessage("ComboMenuItemId is required");
    }
}

internal class GetComboItemsQueryHandler(
    CatalogDbContext dbContext,
    ICurrentUser? currentUser = null) : IQueryHandler<GetComboItemsQuery, GetComboItemsResult>
{
    public async Task<GetComboItemsResult> Handle(GetComboItemsQuery query, CancellationToken cancellationToken)
    {
        Guid? restaurantId = await dbContext.MenuItems
            .AsNoTracking()
            .Where(m => m.Id == query.ComboMenuItemId && !m.IsDeleted)
            .Select(m => (Guid?)m.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);
        if (restaurantId is not null)
        {
            currentUser.EnsureTenantAccess(restaurantId.Value);
        }

        List<ComboItem>? comboItems = await dbContext.ComboItems
            .AsNoTracking()
            .Where(c => c.ComboMenuItemId == query.ComboMenuItemId)
            .ToListAsync(cancellationToken);

        IEnumerable<ComboItemDto>? dtos = comboItems.Adapt<IEnumerable<ComboItemDto>>();

        return new GetComboItemsResult(dtos);
    }
}
