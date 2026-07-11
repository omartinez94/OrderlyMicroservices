namespace Catalog.API.Features.Tables.UpdateTable;

public record UpdateTableCommand(
    Guid Id,
    string TableNumber,
    int Capacity,
    string Shape,
    int PositionX,
    int PositionY,
    TableStatus Status,
    Guid? CurrentOrderId) : ICommand<UpdateTableResult>;

public record UpdateTableResult(bool IsSuccess);

public class UpdateTableCommandValidator : AbstractValidator<UpdateTableCommand>
{
    public UpdateTableCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required");
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

internal class UpdateTableCommandHandler(
    CatalogDbContext dbContext,
    IOutboxPublisher outbox,
    IFeatureManager featureManager) : ICommandHandler<UpdateTableCommand, UpdateTableResult>
{
    public async Task<UpdateTableResult> Handle(UpdateTableCommand command, CancellationToken cancellationToken)
    {
        var table = await dbContext.Tables.FindAsync([command.Id], cancellationToken)
            ?? throw new TableNotFoundException(command.Id);

        // Capture the prior status so we only emit TableStatusChanged when the
        // status actually flipped — the consumer (Ordering reservation/order
        // placement) only cares about the transition, not table renumbering.
        var previousStatus = table.Status;

        table.TableNumber = command.TableNumber;
        table.Capacity = command.Capacity;
        table.Shape = command.Shape;
        table.PositionX = command.PositionX;
        table.PositionY = command.PositionY;
        table.Status = command.Status;
        table.CurrentOrderId = command.CurrentOrderId;

        await dbContext.SaveChangesAsync(cancellationToken);

        if (previousStatus != command.Status &&
            await featureManager.IsEnabledAsync("CatalogMenuEvents", cancellationToken).ConfigureAwait(false))
        {
            await outbox.PublishAsync(new TableStatusChangedIntegrationEvent
            {
                TableId = table.Id,
                RestaurantId = table.RestaurantId,
                NewStatus = command.Status.ToString(),
                CurrentOrderId = table.CurrentOrderId,
            }, cancellationToken).ConfigureAwait(false);
        }

        return new UpdateTableResult(true);
    }
}
