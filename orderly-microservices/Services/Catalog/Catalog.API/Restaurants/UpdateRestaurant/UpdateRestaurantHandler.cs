namespace Catalog.API.Restaurants.UpdateRestaurant;

public record UpdateRestaurantCommand(
    Guid Id,
    int BrandId,
    string Name,
    string Address,
    string PhoneNumber,
    string Email,
    decimal TaxRate,
    string Currency,
    string TimeZone,
    bool AutoConfirmOrders,
    bool AutoConfirmReservations,
    bool AllowAutoSubstitute,
    int EstimatedTurnoverMinutes) : ICommand<UpdateRestaurantResult>;

public record UpdateRestaurantResult(bool IsSuccess);

internal class UpdateRestaurantCommandHandler(IDocumentSession session, ILogger<UpdateRestaurantCommandHandler> logger) : ICommandHandler<UpdateRestaurantCommand, UpdateRestaurantResult>
{
    public async Task<UpdateRestaurantResult> Handle(UpdateRestaurantCommand command, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Updating restaurant with Id: {Id}", command.Id);
        }

        var restaurant = await session.LoadAsync<Restaurant>(command.Id, cancellationToken);
        if (restaurant is null)
        {
            throw new RestaurantNotFoundException(command.Id);
        }

        restaurant.BrandId = command.BrandId;
        restaurant.Name = command.Name;
        restaurant.Address = command.Address;
        restaurant.PhoneNumber = command.PhoneNumber;
        restaurant.Email = command.Email;
        restaurant.TaxRate = command.TaxRate;
        restaurant.Currency = command.Currency;
        restaurant.TimeZone = command.TimeZone;
        restaurant.AutoConfirmOrders = command.AutoConfirmOrders;
        restaurant.AutoConfirmReservations = command.AutoConfirmReservations;
        restaurant.AllowAutoSubstitute = command.AllowAutoSubstitute;
        restaurant.EstimatedTurnoverMinutes = command.EstimatedTurnoverMinutes;

        session.Update(restaurant);
        await session.SaveChangesAsync(cancellationToken);

        return new UpdateRestaurantResult(true);
    }
}
