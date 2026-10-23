using Catalog.API.Features.MenuItems.GetMenuItems;
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItems.GetMenuItemById;

public record GetMenuItemByIdQuery(string Identifier) : IQuery<GetMenuItemByIdResult>
{
    public GetMenuItemByIdQuery(Guid id) : this(id.ToString()) { }

    public Guid Id => Guid.TryParse(Identifier, out Guid guid) ? guid : Guid.Empty;
}

public record GetMenuItemByIdResult(MenuItemDto MenuItem);

public class GetMenuItemByIdQueryValidator : AbstractValidator<GetMenuItemByIdQuery>
{
    public GetMenuItemByIdQueryValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().WithMessage("Identifier is required");
    }
}

internal class GetMenuItemByIdQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetMenuItemByIdQuery, GetMenuItemByIdResult>
{
    public async Task<GetMenuItemByIdResult> Handle(GetMenuItemByIdQuery query, CancellationToken cancellationToken)
    {
        MenuItem? menuItem;

        if (Guid.TryParse(query.Identifier, out Guid guidId))
        {
            menuItem = await dbContext.MenuItems
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == guidId, cancellationToken);
        }
        else
        {
            menuItem = await dbContext.MenuItems
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ShortId == query.Identifier, cancellationToken);
        }

        if (menuItem is null)
        {
            throw new NotFoundException(nameof(MenuItem), query.Identifier);
        }

        MenuItemDto? dto = menuItem.Adapt<MenuItemDto>();
        return new GetMenuItemByIdResult(dto);
    }
}
