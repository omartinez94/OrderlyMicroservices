using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItemVariations.GetMenuItemVariations;

public record GetMenuItemVariationsQuery(Guid MenuItemId) : IQuery<GetMenuItemVariationsResult>;

public record GetMenuItemVariationsResult(IEnumerable<MenuItemVariationDto> Variations);

internal class GetMenuItemVariationsQueryHandler(
    CatalogDbContext dbContext,
    ICurrentUser? currentUser = null) : IQueryHandler<GetMenuItemVariationsQuery, GetMenuItemVariationsResult>
{
    public async Task<GetMenuItemVariationsResult> Handle(GetMenuItemVariationsQuery query, CancellationToken cancellationToken)
    {
        Guid? menuItemRestaurantId = await dbContext.MenuItems
            .AsNoTracking()
            .Where(m => m.Id == query.MenuItemId && !m.IsDeleted)
            .Select(m => (Guid?)m.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);
        bool menuItemExists = menuItemRestaurantId is not null;
        if (!menuItemExists)
        {
            throw new NotFoundException("MenuItem", query.MenuItemId);
        }

        currentUser.EnsureTenantAccess(menuItemRestaurantId!.Value);

        List<MenuItemVariation>? variations = await dbContext.MenuItemVariations
            .AsNoTracking()
            .Where(v => v.MenuItemId == query.MenuItemId && !v.IsDeleted)
            .OrderBy(v => v.DisplayOrder)
            .ToListAsync(cancellationToken);

        IEnumerable<MenuItemVariationDto>? dtos = variations.Adapt<IEnumerable<MenuItemVariationDto>>();

        return new GetMenuItemVariationsResult(dtos);
    }
}
