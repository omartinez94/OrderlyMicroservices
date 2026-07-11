namespace Catalog.API.Features.Restaurants.UpdateRestaurant;

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

internal class UpdateRestaurantCommandHandler(
    CatalogDbContext dbContext,
    IOutboxPublisher outbox,
    IFeatureManager featureManager,
    IPriceHistoryRecorder priceHistory,
    ICurrentUser currentUser) : ICommandHandler<UpdateRestaurantCommand, UpdateRestaurantResult>
{
    public async Task<UpdateRestaurantResult> Handle(UpdateRestaurantCommand command, CancellationToken cancellationToken)
    {
        var restaurant = await dbContext.Restaurants.FindAsync([command.Id], cancellationToken) ?? throw new RestaurantNotFoundException(command.Id);

        // Track which configuration columns actually changed so the
        // RestaurantConfigurationChangedIntegrationEvent carries only the
        // names that consumers care about (Identity re-issues claims when
        // role-bound config flips; Discount deactivates coupons whose
        // currency no longer matches; Notification refreshes receipt
        // templates for tax/currency placeholders).
        var changedFields = new List<string>(capacity: 6);
        if (restaurant.TaxRate != command.TaxRate) changedFields.Add(nameof(restaurant.TaxRate));
        if (restaurant.Currency != command.Currency) changedFields.Add(nameof(restaurant.Currency));
        if (restaurant.TimeZone != command.TimeZone) changedFields.Add(nameof(restaurant.TimeZone));
        if (restaurant.AutoConfirmReservations != command.AutoConfirmReservations) changedFields.Add(nameof(restaurant.AutoConfirmReservations));
        if (restaurant.AllowAutoSubstitute != command.AllowAutoSubstitute) changedFields.Add(nameof(restaurant.AllowAutoSubstitute));
        if (restaurant.EstimatedTurnoverMinutes != command.EstimatedTurnoverMinutes) changedFields.Add(nameof(restaurant.EstimatedTurnoverMinutes));

        var oldTaxRate = restaurant.TaxRate;
        var oldEstimatedTurnoverMinutes = restaurant.EstimatedTurnoverMinutes;

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

        // Phase 4: append a PriceHistory-style audit row per numeric field
        // that changed. Boolean / string / int fields are not represented
        // by old/new numeric values, so they don't write rows — the
        // integration event below already carries the changed-field names.
        if (oldTaxRate != command.TaxRate)
        {
            priceHistory.Record(
                restaurantId: restaurant.Id,
                priceType: PriceType.RestaurantConfiguration,
                oldPrice: oldTaxRate,
                newPrice: command.TaxRate,
                reason: "TaxRate",
                changedByUserId: currentUser.UserId,
                ct: cancellationToken);
        }
        if (oldEstimatedTurnoverMinutes != command.EstimatedTurnoverMinutes)
        {
            priceHistory.Record(
                restaurantId: restaurant.Id,
                priceType: PriceType.RestaurantConfiguration,
                oldPrice: oldEstimatedTurnoverMinutes,
                newPrice: command.EstimatedTurnoverMinutes,
                reason: "EstimatedTurnoverMinutes",
                changedByUserId: currentUser.UserId,
                ct: cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (changedFields.Count > 0 &&
            await featureManager.IsEnabledAsync("CatalogMenuEvents", cancellationToken).ConfigureAwait(false))
        {
            await outbox.PublishAsync(new RestaurantConfigurationChangedIntegrationEvent
            {
                RestaurantId = restaurant.Id,
                ChangedFields = changedFields,
            }, cancellationToken).ConfigureAwait(false);
        }

        return new UpdateRestaurantResult(true);
    }
}
