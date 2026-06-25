namespace Catalog.API.Features.MenuItems.GetMenuItems;

public class GetMenuItemsQuery : IQuery<GetMenuItemsResult>
{
    public Guid RestaurantId { get; set; }
    public int? SubCategoryId { get; set; }
    public bool? IsAvailable { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public record GetMenuItemsResult(IEnumerable<MenuItemDto> Items, int TotalCount, int PageNumber, int PageSize);

public record MenuItemDto(
    Guid Id,
    Guid RestaurantId,
    int? SubCategoryId,
    string Name,
    string Description,
    decimal BasePrice,
    string ImageUrl,
    int PrepTimeMinutes,
    int PrepTimeMaxMinutes,
    ItemType ItemType,
    bool IsAvailable,
    AvailabilityStatus AvailabilityStatus,
    LocalDate? SeasonStartDate,
    LocalDate? SeasonEndDate,
    decimal? PromoPrice,
    Instant? PromoStartDate,
    Instant? PromoEndDate,
    int DisplayOrder);

public class GetMenuItemsQueryValidator : AbstractValidator<GetMenuItemsQuery>
{
    public GetMenuItemsQueryValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage("PageNumber must be greater than 0");
        RuleFor(x => x.PageSize).GreaterThan(0).WithMessage("PageSize must be greater than 0");
    }
}

internal class GetMenuItemsQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetMenuItemsQuery, GetMenuItemsResult>
{
    public async Task<GetMenuItemsResult> Handle(GetMenuItemsQuery query, CancellationToken cancellationToken)
    {
        var dbQuery = dbContext.MenuItems
            .AsNoTracking()
            .Where(m => m.RestaurantId == query.RestaurantId);

        if (query.SubCategoryId.HasValue)
        {
            dbQuery = dbQuery.Where(m => m.SubCategoryId == query.SubCategoryId.Value);
        }

        if (query.IsAvailable.HasValue)
        {
            dbQuery = dbQuery.Where(m => m.IsAvailable == query.IsAvailable.Value);
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var items = await dbQuery
            .OrderBy(m => m.DisplayOrder)
            .ThenBy(m => m.Name)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Adapt<IEnumerable<MenuItemDto>>();

        return new GetMenuItemsResult(dtos, totalCount, query.PageNumber, query.PageSize);
    }
}
