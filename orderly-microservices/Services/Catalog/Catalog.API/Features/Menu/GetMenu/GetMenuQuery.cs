namespace Catalog.API.Features.Menu.GetMenu;

/// <summary>
/// Query to retrieve the full menu tree snapshot for a restaurant.
/// </summary>
public sealed record GetMenuQuery(Guid RestaurantId) : IQuery<GetMenuResult>;

/// <summary>
/// Result wrapping the <see cref="MenuSnapshot"/>.
/// </summary>
public sealed record GetMenuResult(MenuSnapshot Snapshot);
