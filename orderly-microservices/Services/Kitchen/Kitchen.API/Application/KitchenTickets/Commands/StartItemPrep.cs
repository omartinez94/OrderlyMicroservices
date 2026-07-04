namespace Kitchen.API.Application.KitchenTickets.Commands;

public record StartItemPrepCommand(Guid TicketId, Guid ItemId) : ICommand<StartItemPrepResult>;

public record StartItemPrepResult(Guid TicketId, Guid ItemId);

/// <summary>
/// Marks a single item as <c>Preparing</c>. Aggregate-event model: no
/// integration event is published — Ordering does not track per-item state
/// and the in-process domain event (<c>KitchenTicketItemPrepStartedEvent</c>)
/// is enough for the SignalR broadcast.
/// </summary>
public class StartItemPrepHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<StartItemPrepHandler> logger)
    : ICommandHandler<StartItemPrepCommand, StartItemPrepResult>
{
    public async Task<StartItemPrepResult> Handle(
        StartItemPrepCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        KitchenTicket ticket = await repository.GetByIdAsync(command.TicketId, cancellationToken)
            ?? throw new KitchenTicketNotFoundException(command.TicketId);

        Instant now = SystemClock.Instance.GetCurrentInstant();
        ticket.StartItemPrep(KitchenItemId.Of(command.ItemId), now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Started prep on item {ItemId} of KitchenTicket {TicketId}.",
            command.ItemId, ticket.Id.Value);

        return new StartItemPrepResult(ticket.Id.Value, command.ItemId);
    }
}