namespace Catalog.API.Features.Menu.GetMenu;

/// <summary>
/// Query to retrieve the full menu tree snapshot for a restaurant by ID or short ID.
/// </summary>
public sealed record GetMenuQuery(string Identifier) : IQuery<GetMenuResult>
{
    public GetMenuQuery(Guid restaurantId) : this(restaurantId.ToString()) { }
}

/// <summary>
/// Result wrapping the <see cref="MenuSnapshot"/>.
/// </summary>
public sealed record GetMenuResult(MenuSnapshot Snapshot);
