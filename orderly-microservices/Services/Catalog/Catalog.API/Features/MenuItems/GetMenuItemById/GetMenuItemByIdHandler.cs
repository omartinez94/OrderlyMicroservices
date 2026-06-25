using Catalog.API.Features.MenuItems.GetMenuItems;
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItems.GetMenuItemById;

public record GetMenuItemByIdQuery(Guid Id) : IQuery<GetMenuItemByIdResult>;

public record GetMenuItemByIdResult(MenuItemDto MenuItem);

public class GetMenuItemByIdQueryValidator : AbstractValidator<GetMenuItemByIdQuery>
{
    public GetMenuItemByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id is required");
    }
}

internal class GetMenuItemByIdQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetMenuItemByIdQuery, GetMenuItemByIdResult>
{
    public async Task<GetMenuItemByIdResult> Handle(GetMenuItemByIdQuery query, CancellationToken cancellationToken)
    {
        var menuItem = await dbContext.MenuItems
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == query.Id, cancellationToken);

        if (menuItem is null)
        {
            throw new NotFoundException(nameof(MenuItem), query.Id);
        }

        var dto = menuItem.Adapt<MenuItemDto>();
        return new GetMenuItemByIdResult(dto);
    }
}
