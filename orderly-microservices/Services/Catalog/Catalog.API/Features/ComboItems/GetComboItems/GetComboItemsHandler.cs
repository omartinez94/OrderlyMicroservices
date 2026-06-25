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

internal class GetComboItemsQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetComboItemsQuery, GetComboItemsResult>
{
    public async Task<GetComboItemsResult> Handle(GetComboItemsQuery query, CancellationToken cancellationToken)
    {
        var comboItems = await dbContext.ComboItems
            .AsNoTracking()
            .Where(c => c.ComboMenuItemId == query.ComboMenuItemId)
            .ToListAsync(cancellationToken);

        var dtos = comboItems.Adapt<IEnumerable<ComboItemDto>>();

        return new GetComboItemsResult(dtos);
    }
}
