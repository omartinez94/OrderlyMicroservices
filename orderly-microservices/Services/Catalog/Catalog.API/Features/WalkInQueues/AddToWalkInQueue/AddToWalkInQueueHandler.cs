namespace Catalog.API.Features.WalkInQueues.AddToWalkInQueue;

public record AddToWalkInQueueCommand(
    Guid RestaurantId,
    string CustomerName,
    string CustomerPhone,
    int PartySize,
    int EstimatedWaitMinutes) : ICommand<AddToWalkInQueueResult>;

public record AddToWalkInQueueResult(int Id);

public class AddToWalkInQueueCommandValidator : AbstractValidator<AddToWalkInQueueCommand>
{
    public AddToWalkInQueueCommandValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.CustomerName)
            .NotEmpty().WithMessage("CustomerName is required")
            .MaximumLength(100).WithMessage("CustomerName must not exceed 100 characters");
        RuleFor(x => x.CustomerPhone)
            .NotEmpty().WithMessage("CustomerPhone is required")
            .MaximumLength(20).WithMessage("CustomerPhone must not exceed 20 characters");
        RuleFor(x => x.PartySize)
            .GreaterThan(0).WithMessage("PartySize must be greater than 0");
        RuleFor(x => x.EstimatedWaitMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("EstimatedWaitMinutes must be non-negative");
    }
}

internal class AddToWalkInQueueCommandHandler(CatalogDbContext dbContext) : ICommandHandler<AddToWalkInQueueCommand, AddToWalkInQueueResult>
{
    public async Task<AddToWalkInQueueResult> Handle(AddToWalkInQueueCommand command, CancellationToken cancellationToken)
    {
        var walkIn = new WalkInQueue
        {
            Id = 0, // auto-increment
            RestaurantId = command.RestaurantId,
            CustomerName = command.CustomerName,
            CustomerPhone = command.CustomerPhone,
            PartySize = command.PartySize,
            EstimatedWaitMinutes = command.EstimatedWaitMinutes,
            Status = WalkInQueueStatus.Waiting
        };

        dbContext.WalkInQueues.Add(walkIn);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AddToWalkInQueueResult(walkIn.Id);
    }
}
