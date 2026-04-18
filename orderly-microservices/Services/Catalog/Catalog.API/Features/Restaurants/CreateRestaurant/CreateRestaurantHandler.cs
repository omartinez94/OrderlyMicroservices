namespace Catalog.API.Features.Restaurants.CreateRestaurant;

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

public class CreateRestaurantCommandValidator : AbstractValidator<CreateRestaurantCommand>
{
    public CreateRestaurantCommandValidator()
    {
        RuleFor(x => x.BrandId)
            .NotEmpty().WithMessage("BrandId is required");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters");
        RuleFor(x => x.Address)
            .NotEmpty().WithMessage("Address is required")
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
