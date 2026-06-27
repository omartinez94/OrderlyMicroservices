namespace Catalog.API.Features.MergedTables.MergeTables;

public record MergeTablesCommand(Guid RestaurantId, Guid ParentTableId, Guid ChildTableId) : ICommand<MergeTablesResult>;

public record MergeTablesResult(Guid Id);

public class MergeTablesCommandValidator : AbstractValidator<MergeTablesCommand>
{
    public MergeTablesCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.ParentTableId).NotEmpty().WithMessage("ParentTableId is required");
        RuleFor(x => x.ChildTableId).NotEmpty().WithMessage("ChildTableId is required");
        RuleFor(x => x.ChildTableId).NotEqual(x => x.ParentTableId).WithMessage("ChildTableId must be different from ParentTableId");
    }
}

internal class MergeTablesCommandHandler(CatalogDbContext dbContext) : ICommandHandler<MergeTablesCommand, MergeTablesResult>
{
    public async Task<MergeTablesResult> Handle(MergeTablesCommand command, CancellationToken cancellationToken)
    {
        // Validation of restaurant/tables could go here.

        var mergedTable = new MergedTable
        {
            Id = Guid.NewGuid(),
            ParentTableId = command.ParentTableId,
            ChildTableId = command.ChildTableId,
            IsActive = true,
            MergedAt = SystemClock.Instance.GetCurrentInstant()
        };

        dbContext.MergedTables.Add(mergedTable);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new MergeTablesResult(mergedTable.Id);
    }
}
