namespace Catalog.API.Features.Reservations.CancelReservation;

public record CancelReservationCommand(Guid Id, string? Reason = null) : ICommand<CancelReservationResult>;

public record CancelReservationResult(bool IsSuccess);

internal class CancelReservationCommandHandler(CatalogDbContext dbContext) : ICommandHandler<CancelReservationCommand, CancelReservationResult>
{
    public async Task<CancelReservationResult> Handle(CancelReservationCommand command, CancellationToken cancellationToken)
    {
        var reservation = await dbContext.Reservations.FindAsync([command.Id], cancellationToken)
            ?? throw new ReservationNotFoundException(command.Id);

        reservation.Status = ReservationStatus.Cancelled;
        reservation.CancelledAt = SystemClock.Instance.GetCurrentInstant();

        if (!string.IsNullOrWhiteSpace(command.Reason))
        {
            reservation.Notes = string.IsNullOrWhiteSpace(reservation.Notes)
                ? $"Cancelled: {command.Reason}"
                : $"{reservation.Notes} | Cancelled: {command.Reason}";
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CancelReservationResult(true);
    }
}
