namespace Kitchen.API.Hubs;

public sealed record MenuItemChangedDto(
    Guid MenuItemId,
    Guid RestaurantId,
    string ChangeType,
    string? Name = null,
    decimal? BasePrice = null,
    bool? IsAvailable = null,
    string? AvailabilityStatus = null,
    string? ImageUrl = null,
    Instant OccurredAt = default);
