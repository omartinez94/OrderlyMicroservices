namespace Catalog.API.Features.Tables.CreateTable;

public record CreateTableCommand(
    Guid RestaurantId,
    string TableNumber,
    int Capacity,
    string Shape,
    int PositionX,
    int PositionY) : ICommand<CreateTableResult>;

public record CreateTableResult(Guid Id);

public class CreateTableCommandValidator : AbstractValidator<CreateTableCommand>
{
    public CreateTableCommandValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.TableNumber)
            .NotEmpty().WithMessage("TableNumber is required")
            .MaximumLength(10).WithMessage("TableNumber must not exceed 10 characters");
        RuleFor(x => x.Capacity)
            .GreaterThan(0).WithMessage("Capacity must be greater than 0");
        RuleFor(x => x.Shape)
            .MaximumLength(20).WithMessage("Shape must not exceed 20 characters");
        RuleFor(x => x.PositionX)
            .GreaterThanOrEqualTo(0).WithMessage("PositionX must be non-negative");
        RuleFor(x => x.PositionY)
            .GreaterThanOrEqualTo(0).WithMessage("PositionY must be non-negative");
    }
}

internal class CreateTableCommandHandler(CatalogDbContext dbContext) : ICommandHandler<CreateTableCommand, CreateTableResult>
{
    public async Task<CreateTableResult> Handle(CreateTableCommand command, CancellationToken cancellationToken)
    {
        var table = new Table
        {
            Id = Guid.NewGuid(),
            RestaurantId = command.RestaurantId,
            TableNumber = command.TableNumber,
            Capacity = command.Capacity,
            Shape = command.Shape,
            PositionX = command.PositionX,
            PositionY = command.PositionY
        };

        dbContext.Tables.Add(table);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateTableResult(table.Id);
    }
}
