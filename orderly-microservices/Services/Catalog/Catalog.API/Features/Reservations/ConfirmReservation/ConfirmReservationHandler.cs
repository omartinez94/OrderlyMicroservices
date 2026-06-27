namespace Catalog.API.Features.Reservations.ConfirmReservation;

public record ConfirmReservationCommand(Guid Id) : ICommand<ConfirmReservationResult>;

public record ConfirmReservationResult(bool IsSuccess);

internal class ConfirmReservationCommandHandler(CatalogDbContext dbContext) : ICommandHandler<ConfirmReservationCommand, ConfirmReservationResult>
{
    public async Task<ConfirmReservationResult> Handle(ConfirmReservationCommand command, CancellationToken cancellationToken)
    {
        var reservation = await dbContext.Reservations.FindAsync([command.Id], cancellationToken)
            ?? throw new ReservationNotFoundException(command.Id);

        reservation.Status = ReservationStatus.Confirmed;
        reservation.ConfirmedAt = SystemClock.Instance.GetCurrentInstant();

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ConfirmReservationResult(true);
    }
}
