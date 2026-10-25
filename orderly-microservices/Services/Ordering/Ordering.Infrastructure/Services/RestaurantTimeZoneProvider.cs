using Ordering.Application.Common.Interfaces;
using Ordering.Application.Data;

namespace Ordering.Infrastructure.Services;

public class RestaurantTimeZoneProvider(IApplicationDbContext dbContext) : IRestaurantTimeZoneProvider
{
    public async Task<RestaurantTimeZoneResolution> GetTimeZoneAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        if (restaurantId == Guid.Empty)
        {
            return new RestaurantTimeZoneResolution(DateTimeZone.Utc, DateTimeZone.Utc.Id, IsFallback: true);
        }

        RestaurantSetting? setting = await dbContext.RestaurantSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(rs => rs.RestaurantId == restaurantId, cancellationToken);

        if (setting is null || string.IsNullOrWhiteSpace(setting.TimeZoneId))
        {
            return new RestaurantTimeZoneResolution(DateTimeZone.Utc, DateTimeZone.Utc.Id, IsFallback: true);
        }

        DateTimeZone? zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(setting.TimeZoneId);
        if (zone is null)
        {
            return new RestaurantTimeZoneResolution(DateTimeZone.Utc, DateTimeZone.Utc.Id, IsFallback: true);
        }

        return new RestaurantTimeZoneResolution(zone, zone.Id, IsFallback: false);
    }
}
