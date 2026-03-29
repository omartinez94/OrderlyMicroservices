namespace Catalog.API.Restaurants.CreateRestaurant;

public record CreateRestaurantCommand(
    Guid BrandId,
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
    int EstimatedTurnoverMinutes) : ICommand<CreateRestaurantResult>;

public record CreateRestaurantResult(Guid Id);

internal class CreateRestaurantCommandHandler(IDocumentSession session) : ICommandHandler<CreateRestaurantCommand, CreateRestaurantResult>
{
    public async Task<CreateRestaurantResult> Handle(CreateRestaurantCommand command, CancellationToken cancellationToken)
    {
        // Business logic to create a restaurant would go here, such as validating the input, saving to a database, etc.

        var restaurant = new Restaurant
        {
            Id = Guid.NewGuid(),
            BrandId = command.BrandId,
            Name = command.Name,
            Address = command.Address,
            PhoneNumber = command.PhoneNumber,
            Email = command.Email,
            TaxRate = command.TaxRate,
            Currency = command.Currency,
            TimeZone = command.TimeZone,
            AutoConfirmOrders = command.AutoConfirmOrders,
            AutoConfirmReservations = command.AutoConfirmReservations,
            AllowAutoSubstitute = command.AllowAutoSubstitute,
            EstimatedTurnoverMinutes = command.EstimatedTurnoverMinutes
        };

        if (restaurant is IAuditableEntity auditableEntity)
        {
            auditableEntity.CreatedFrom("system", SystemClock.Instance.GetCurrentInstant());
        }

        session.Store(restaurant);
        await session.SaveChangesAsync(cancellationToken);

        return new CreateRestaurantResult(restaurant.Id);
    }
}
