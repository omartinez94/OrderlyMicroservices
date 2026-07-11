namespace BuildingBlocks.Enums;

public enum ItemType
{
    Regular,
    Combo,
    Promo,
    Seasonal
}

public enum AvailabilityStatus
{
    Available,
    Limited,
    Unavailable
}

public enum PriceType
{
    BasePrice,
    Variation,
    IngredientAlternative,

    /// <summary>
    /// Audit row emitted by <c>UpdateRestaurantHandler</c> when
    /// configuration fields change. <c>OldPrice</c> / <c>NewPrice</c>
    /// are populated only for numeric fields (e.g. <c>TaxRate</c>); the
    /// changed-field name lives in <c>PriceHistory.Reason</c>.
    /// </summary>
    RestaurantConfiguration
}
