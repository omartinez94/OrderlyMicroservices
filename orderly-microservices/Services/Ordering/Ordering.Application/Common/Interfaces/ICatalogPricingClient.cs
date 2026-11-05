namespace Ordering.Application.Common.Interfaces;

public record CatalogMenuSnapshot(
    Guid RestaurantId,
    IReadOnlyList<CatalogMenuItemDto> Items);

public record CatalogMenuItemDto(
    Guid Id,
    string Name,
    decimal BasePrice,
    decimal? PromoPrice,
    bool IsAvailable,
    string AvailabilityStatus,
    IReadOnlyList<CatalogMenuItemVariationDto> Variations,
    int? DefaultCourseNumber);

public record CatalogMenuItemVariationDto(
    int Id,
    string Name,
    string VariationValue,
    decimal PriceModifier,
    bool IsDefault);

public record CatalogTableDto(
    Guid Id,
    Guid RestaurantId,
    string TableNumber,
    int Capacity,
    string Status);

public interface ICatalogPricingClient
{
    Task<CatalogMenuSnapshot> GetMenuSnapshotAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task<CatalogTableDto?> GetTableAsync(Guid restaurantId, Guid tableId, CancellationToken cancellationToken = default);
}
