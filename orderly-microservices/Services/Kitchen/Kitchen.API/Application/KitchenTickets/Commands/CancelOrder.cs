namespace Kitchen.API.Application.KitchenTickets.Commands;

public record CancelOrderCommand(Guid TicketId, string Reason) : ICommand<CancelOrderResult>;

public record CancelOrderResult(Guid TicketId, Instant CancelledAt);

public class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(c => c.TicketId).NotEmpty();
        RuleFor(c => c.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}

/// <summary>
/// Cancels the ticket from any non-terminal state. Publishes
/// <see cref="KitchenOrderCancelledIntegrationEvent"/> so Ordering can drive
/// <c>Order.Cancel</c>.
/// </summary>
public class CancelOrderHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IPublishEndpoint publishEndpoint,
    ICurrentUser currentUser,
    ILogger<CancelOrderHandler> logger)
    : ICommandHandler<CancelOrderCommand, CancelOrderResult>
{
    public async Task<CancelOrderResult> Handle(
        CancelOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid staffUserId = currentUser.UserId
            ?? throw new KitchenDomainException(
                "Authenticated staff user id is required to cancel a ticket.",
                nameof(currentUser));

        KitchenTicket ticket = await repository.GetByIdAsync(command.TicketId, cancellationToken)
            ?? throw new KitchenTicketNotFoundException(command.TicketId);

        Instant now = SystemClock.Instance.GetCurrentInstant();
        ticket.Cancel(command.Reason, staffUserId, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await publishEndpoint.Publish(
            new KitchenOrderCancelledIntegrationEvent
            {
                OrderId = ticket.Id.Value,
                Reason = command.Reason,
                CancelledByUserId = staffUserId,
                CancelledAt = now,
            },
            cancellationToken);

        logger.LogInformation(
            "Cancelled KitchenTicket {TicketId} (Order {OrderNumber}) by user {UserId}: {Reason}.",
            ticket.Id.Value, ticket.OrderNumber, staffUserId, command.Reason);

        return new CancelOrderResult(ticket.Id.Value, now);
    }
}