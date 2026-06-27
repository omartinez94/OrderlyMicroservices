namespace Catalog.API.Features.Tables.DeleteTable;

public record DeleteTableCommand(Guid Id) : ICommand<DeleteTableResult>;

public record DeleteTableResult(bool IsSuccess);

internal class DeleteTableCommandHandler(CatalogDbContext dbContext) : ICommandHandler<DeleteTableCommand, DeleteTableResult>
{
    public async Task<DeleteTableResult> Handle(DeleteTableCommand command, CancellationToken cancellationToken)
    {
        var table = await dbContext.Tables.FindAsync([command.Id], cancellationToken)
            ?? throw new TableNotFoundException(command.Id);

        dbContext.Tables.Remove(table);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteTableResult(true);
    }
}
