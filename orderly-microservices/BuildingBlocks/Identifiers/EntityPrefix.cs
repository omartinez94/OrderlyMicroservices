namespace BuildingBlocks.Identifiers;

/// <summary>
/// Canonical entity prefix enumeration for typed short identifiers.
/// </summary>
public enum EntityPrefix
{
    Restaurant,
    Order,
    Brand,
    Table,
    MenuItem,
    MenuCategory,
    Reservation,
    WalkInQueue,
    User,
    KitchenStation
}

/// <summary>
/// Contiguous 3-letter prefix string constants for typed short identifiers.
/// </summary>
public static class EntityPrefixConstants
{
    public const string Restaurant = "rst";
    public const string Order = "ord";
    public const string Brand = "brd";
    public const string Table = "tbl";
    public const string MenuItem = "itm";
    public const string MenuCategory = "cat";
    public const string Reservation = "rsv";
    public const string WalkInQueue = "que";
    public const string User = "usr";
    public const string KitchenStation = "stn";
}

/// <summary>
/// Extension methods for <see cref="EntityPrefix"/>.
/// </summary>
public static class EntityPrefixExtensions
{
    public static string ToPrefixString(this EntityPrefix prefix) => prefix switch
    {
        EntityPrefix.Restaurant => EntityPrefixConstants.Restaurant,
        EntityPrefix.Order => EntityPrefixConstants.Order,
        EntityPrefix.Brand => EntityPrefixConstants.Brand,
        EntityPrefix.Table => EntityPrefixConstants.Table,
        EntityPrefix.MenuItem => EntityPrefixConstants.MenuItem,
        EntityPrefix.MenuCategory => EntityPrefixConstants.MenuCategory,
        EntityPrefix.Reservation => EntityPrefixConstants.Reservation,
        EntityPrefix.WalkInQueue => EntityPrefixConstants.WalkInQueue,
        EntityPrefix.User => EntityPrefixConstants.User,
        EntityPrefix.KitchenStation => EntityPrefixConstants.KitchenStation,
        _ => throw new ArgumentOutOfRangeException(nameof(prefix), prefix, $"Unknown entity prefix: {prefix}")
    };
}
