using BuildingBlocks.Messaging.Events.Catalog;
using MassTransit;

namespace Ordering.Application.Orders.EventHandlers.Integration;

public class RestaurantConfigurationChangedIntegrationEventHandler(
    IApplicationDbContext dbContext,
    IClock clock,
    ILogger<RestaurantConfigurationChangedIntegrationEventHandler> logger)
    : IConsumer<RestaurantConfigurationChangedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<RestaurantConfigurationChangedIntegrationEvent> context)
    {
        RestaurantConfigurationChangedIntegrationEvent message = context.Message;

        if (message.RestaurantId == Guid.Empty || string.IsNullOrWhiteSpace(message.TimeZone))
        {
            return;
        }

        DateTimeZone? validatedZone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(message.TimeZone);
        if (validatedZone is null)
        {
            logger.LogWarning(
                "Ignoring unknown IANA time zone '{TimeZone}' for restaurant {RestaurantId}",
                message.TimeZone,
                message.RestaurantId);
            return;
        }

        Instant now = clock.GetCurrentInstant();
        RestaurantSetting? existing = await dbContext.RestaurantSettings
            .SingleOrDefaultAsync(rs => rs.RestaurantId == message.RestaurantId, context.CancellationToken);

        if (existing is null)
        {
            RestaurantSetting created = new()
            {
                RestaurantId = message.RestaurantId,
                TimeZoneId = validatedZone.Id,
                UpdatedAt = now,
            };
            dbContext.RestaurantSettings.Add(created);
        }
        else
        {
            existing.TimeZoneId = validatedZone.Id;
            existing.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
