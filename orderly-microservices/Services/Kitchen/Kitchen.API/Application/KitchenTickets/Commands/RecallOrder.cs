namespace Kitchen.API.Application.KitchenTickets.Commands;

public record RecallOrderCommand(Guid TicketId) : ICommand<RecallOrderResult>;

public record RecallOrderResult(Guid TicketId);

/// <summary>
/// Chef pull-back: <c>Bumped</c> → <c>Ready</c>. Local state only — no
/// integration event per the plan (Ordering does not need to react).
/// </summary>
public class RecallOrderHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<RecallOrderHandler> logger)
    : ICommandHandler<RecallOrderCommand, RecallOrderResult>
{
    public async Task<RecallOrderResult> Handle(
        RecallOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        KitchenTicket ticket = await repository.GetByIdAsync(command.TicketId, cancellationToken)
            ?? throw new KitchenTicketNotFoundException(command.TicketId);

        Instant now = SystemClock.Instance.GetCurrentInstant();
        ticket.Recall(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Recalled KitchenTicket {TicketId} (Order {OrderNumber}).",
            ticket.Id.Value, ticket.OrderNumber);

        return new RecallOrderResult(ticket.Id.Value);
    }
}