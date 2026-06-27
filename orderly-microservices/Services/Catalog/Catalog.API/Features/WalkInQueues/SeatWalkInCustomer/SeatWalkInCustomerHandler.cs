namespace Catalog.API.Features.WalkInQueues.SeatWalkInCustomer;

public record SeatWalkInCustomerCommand(int Id, Guid TableId) : ICommand<SeatWalkInCustomerResult>;

public record SeatWalkInCustomerResult(bool IsSuccess);

internal class SeatWalkInCustomerCommandHandler(CatalogDbContext dbContext) : ICommandHandler<SeatWalkInCustomerCommand, SeatWalkInCustomerResult>
{
    public async Task<SeatWalkInCustomerResult> Handle(SeatWalkInCustomerCommand command, CancellationToken cancellationToken)
    {
        var walkIn = await dbContext.WalkInQueues.FindAsync([command.Id], cancellationToken)
            ?? throw new WalkInQueueNotFoundException(command.Id);

        walkIn.Status = WalkInQueueStatus.Seated;
        walkIn.SeatedAt = SystemClock.Instance.GetCurrentInstant();
        walkIn.TableId = command.TableId;

        // Also update the table status
        var table = await dbContext.Tables.FindAsync([command.TableId], cancellationToken);
        if (table != null)
        {
            table.Status = TableStatus.Occupied;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new SeatWalkInCustomerResult(true);
    }
}
