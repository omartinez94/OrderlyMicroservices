namespace Catalog.API.Features.Reservations.CreateReservation;

public record CreateReservationCommand(
    Guid RestaurantId,
    string CustomerName,
    string CustomerPhone,
    string CustomerEmail,
    LocalDate ReservationDate,
    LocalTime ReservationTime,
    int PartySize,
    bool RequiresApproval,
    string SpecialRequests,
    string Notes) : ICommand<CreateReservationResult>;

public record CreateReservationResult(Guid Id);

public class CreateReservationCommandValidator : AbstractValidator<CreateReservationCommand>
{
    public CreateReservationCommandValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.CustomerName)
            .NotEmpty().WithMessage("CustomerName is required")
            .MaximumLength(100).WithMessage("CustomerName must not exceed 100 characters");
        RuleFor(x => x.CustomerPhone)
            .NotEmpty().WithMessage("CustomerPhone is required")
            .MaximumLength(20).WithMessage("CustomerPhone must not exceed 20 characters");
        RuleFor(x => x.CustomerEmail)
            .EmailAddress().WithMessage("CustomerEmail must be a valid email address")
            .When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail));
        RuleFor(x => x.ReservationDate)
            .NotEmpty().WithMessage("ReservationDate is required");
        RuleFor(x => x.PartySize)
            .GreaterThan(0).WithMessage("PartySize must be greater than 0");
    }
}

internal class CreateReservationCommandHandler(CatalogDbContext dbContext) : ICommandHandler<CreateReservationCommand, CreateReservationResult>
{
    public async Task<CreateReservationResult> Handle(CreateReservationCommand command, CancellationToken cancellationToken)
    {
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            RestaurantId = command.RestaurantId,
            CustomerName = command.CustomerName,
            CustomerPhone = command.CustomerPhone,
            CustomerEmail = command.CustomerEmail,
            ReservationDate = command.ReservationDate,
            ReservationTime = command.ReservationTime,
            PartySize = command.PartySize,
            RequiresApproval = command.RequiresApproval,
            SpecialRequests = command.SpecialRequests,
            Notes = command.Notes,
            Status = command.RequiresApproval ? ReservationStatus.Pending : ReservationStatus.Confirmed
        };

        dbContext.Reservations.Add(reservation);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateReservationResult(reservation.Id);
    }
}
