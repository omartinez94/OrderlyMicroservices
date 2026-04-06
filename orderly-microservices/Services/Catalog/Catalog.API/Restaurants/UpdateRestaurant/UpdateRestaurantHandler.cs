namespace Catalog.API.Restaurants.UpdateRestaurant;

public record UpdateRestaurantCommand(
    Guid Id,
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
    int EstimatedTurnoverMinutes) : ICommand<UpdateRestaurantResult>;

public record UpdateRestaurantResult(bool IsSuccess);

public class UpdateRestaurantCommandValidator : AbstractValidator<UpdateRestaurantCommand>
{
    public UpdateRestaurantCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required");
        RuleFor(x => x.BrandId)
            .NotEmpty().WithMessage("BrandId is required");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters");
        RuleFor(x => x.Address).
            NotEmpty().WithMessage("Address is required")
            .MaximumLength(200).WithMessage("Address must not exceed 200 characters");
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("PhoneNumber is required")
            .MaximumLength(20).WithMessage("PhoneNumber must not exceed 20 characters");
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email must be a valid email address")
            .MaximumLength(100).WithMessage("Email must not exceed 100 characters");
        RuleFor(x => x.TaxRate)
            .GreaterThanOrEqualTo(0).WithMessage("TaxRate must be greater than or equal to 0")
            .LessThanOrEqualTo(1).WithMessage("TaxRate must be less than or equal to 1");
        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required")
            .Length(3).WithMessage("Currency must be exactly 3 characters");
        RuleFor(x => x.TimeZone)
            .NotEmpty().WithMessage("TimeZone is required");
        RuleFor(x => x.EstimatedTurnoverMinutes)
            .GreaterThan(0).WithMessage("EstimatedTurnoverMinutes must be greater than 0");
    }
}

internal class UpdateRestaurantCommandHandler(IDocumentSession session) : ICommandHandler<UpdateRestaurantCommand, UpdateRestaurantResult>
{
    public async Task<UpdateRestaurantResult> Handle(UpdateRestaurantCommand command, CancellationToken cancellationToken)
    {
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

        if (restaurant is IAuditableEntity auditableRest)
        {
            auditableRest.ModifiedFrom("system", SystemClock.Instance.GetCurrentInstant());
        }

        session.Update(restaurant);
        await session.SaveChangesAsync(cancellationToken);

        return new UpdateRestaurantResult(true);
    }
}
