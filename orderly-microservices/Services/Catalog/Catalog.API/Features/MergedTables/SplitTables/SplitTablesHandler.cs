namespace Catalog.API.Features.MergedTables.SplitTables;

public record SplitTablesCommand(Guid RestaurantId, Guid Id) : ICommand<SplitTablesResult>;

public record SplitTablesResult(bool Success);

public class SplitTablesCommandValidator : AbstractValidator<SplitTablesCommand>
{
    public SplitTablesCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id is required");
    }
}

internal class SplitTablesCommandHandler(CatalogDbContext dbContext) : ICommandHandler<SplitTablesCommand, SplitTablesResult>
{
    public async Task<SplitTablesResult> Handle(SplitTablesCommand command, CancellationToken cancellationToken)
    {
        var mergedTable = await dbContext.MergedTables.FindAsync([command.Id], cancellationToken: cancellationToken);

        if (mergedTable is null)
        {
            throw new Exception($"MergedTable {command.Id} not found");
        }

        mergedTable.IsActive = false;
        mergedTable.SplitAt = SystemClock.Instance.GetCurrentInstant();

        dbContext.MergedTables.Update(mergedTable);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SplitTablesResult(true);
    }
}
