namespace Catalog.API.Features.WalkInQueues.RemoveFromQueue;

public record RemoveFromQueueCommand(int Id) : ICommand<RemoveFromQueueResult>;

public record RemoveFromQueueResult(bool IsSuccess);

internal class RemoveFromQueueCommandHandler(CatalogDbContext dbContext) : ICommandHandler<RemoveFromQueueCommand, RemoveFromQueueResult>
{
    public async Task<RemoveFromQueueResult> Handle(RemoveFromQueueCommand command, CancellationToken cancellationToken)
    {
        var walkIn = await dbContext.WalkInQueues.FindAsync([command.Id], cancellationToken)
            ?? throw new WalkInQueueNotFoundException(command.Id);

        walkIn.Status = WalkInQueueStatus.Cancelled;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RemoveFromQueueResult(true);
    }
}
