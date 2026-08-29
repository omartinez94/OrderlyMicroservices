using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItemVariations.GetMenuItemVariations;

public record GetMenuItemVariationsQuery(Guid MenuItemId) : IQuery<GetMenuItemVariationsResult>;

public record GetMenuItemVariationsResult(IEnumerable<MenuItemVariationDto> Variations);

internal class GetMenuItemVariationsQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetMenuItemVariationsQuery, GetMenuItemVariationsResult>
{
    public async Task<GetMenuItemVariationsResult> Handle(GetMenuItemVariationsQuery query, CancellationToken cancellationToken)
    {
        bool menuItemExists = await dbContext.MenuItems.AnyAsync(m => m.Id == query.MenuItemId && !m.IsDeleted, cancellationToken);
        if (!menuItemExists)
        {
            throw new NotFoundException("MenuItem", query.MenuItemId);
        }

        List<MenuItemVariation>? variations = await dbContext.MenuItemVariations
            .AsNoTracking()
            .Where(v => v.MenuItemId == query.MenuItemId && !v.IsDeleted)
            .OrderBy(v => v.DisplayOrder)
            .ToListAsync(cancellationToken);

        IEnumerable<MenuItemVariationDto>? dtos = variations.Adapt<IEnumerable<MenuItemVariationDto>>();

        return new GetMenuItemVariationsResult(dtos);
    }
}
