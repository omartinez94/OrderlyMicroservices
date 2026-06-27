namespace Catalog.API.Features.Reservations.SeatReservation;

public record SeatReservationCommand(Guid Id, Guid? TableId = null) : ICommand<SeatReservationResult>;

public record SeatReservationResult(bool IsSuccess);

internal class SeatReservationCommandHandler(CatalogDbContext dbContext) : ICommandHandler<SeatReservationCommand, SeatReservationResult>
{
    public async Task<SeatReservationResult> Handle(SeatReservationCommand command, CancellationToken cancellationToken)
    {
        var reservation = await dbContext.Reservations.FindAsync([command.Id], cancellationToken)
            ?? throw new ReservationNotFoundException(command.Id);

        reservation.Status = ReservationStatus.Seated;
        reservation.SeatedAt = SystemClock.Instance.GetCurrentInstant();
        if (command.TableId.HasValue)
        {
            reservation.TableId = command.TableId.Value;

            // Also update the table status
            var table = await dbContext.Tables.FindAsync([command.TableId.Value], cancellationToken);
            if (table != null)
            {
                table.Status = TableStatus.Occupied;
                table.CurrentOrderId = null; // CurrentOrderId would be set when an order is created
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new SeatReservationResult(true);
    }
}
