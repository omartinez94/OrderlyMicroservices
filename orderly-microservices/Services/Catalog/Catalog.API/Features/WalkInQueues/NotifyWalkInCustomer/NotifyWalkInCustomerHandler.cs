namespace Catalog.API.Features.WalkInQueues.NotifyWalkInCustomer;

public record NotifyWalkInCustomerCommand(int Id) : ICommand<NotifyWalkInCustomerResult>;

public record NotifyWalkInCustomerResult(bool IsSuccess);

internal class NotifyWalkInCustomerCommandHandler(CatalogDbContext dbContext) : ICommandHandler<NotifyWalkInCustomerCommand, NotifyWalkInCustomerResult>
{
    public async Task<NotifyWalkInCustomerResult> Handle(NotifyWalkInCustomerCommand command, CancellationToken cancellationToken)
    {
        var walkIn = await dbContext.WalkInQueues.FindAsync([command.Id], cancellationToken)
            ?? throw new WalkInQueueNotFoundException(command.Id);

        walkIn.Status = WalkInQueueStatus.Notified;
        walkIn.NotifiedAt = SystemClock.Instance.GetCurrentInstant();

        await dbContext.SaveChangesAsync(cancellationToken);

        return new NotifyWalkInCustomerResult(true);
    }
}
