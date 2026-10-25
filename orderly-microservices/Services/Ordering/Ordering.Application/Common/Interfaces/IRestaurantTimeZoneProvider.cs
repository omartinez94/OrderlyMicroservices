namespace Ordering.Application.Common.Interfaces;

public record RestaurantTimeZoneResolution(DateTimeZone Zone, string TimeZoneId, bool IsFallback);

public interface IRestaurantTimeZoneProvider
{
    Task<RestaurantTimeZoneResolution> GetTimeZoneAsync(Guid restaurantId, CancellationToken cancellationToken = default);
}
