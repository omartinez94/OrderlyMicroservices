namespace Kitchen.API.Application.KitchenTickets.Commands;

public record MarkItemReadyCommand(Guid TicketId, Guid ItemId) : ICommand<MarkItemReadyResult>;

public record MarkItemReadyResult(Guid TicketId, Guid ItemId);

/// <summary>
/// Marks a single item as <c>Ready</c>. Aggregate-event model: no
/// integration event is published — Ordering does not track per-item state
/// and the in-process domain event (<c>KitchenTicketItemReadyEvent</c>)
/// is enough for the SignalR broadcast. The aggregate transitions to
/// <c>Ready</c> only when every item is ready (see <c>MarkOrderReady</c>).
/// </summary>
public class MarkItemReadyHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<MarkItemReadyHandler> logger)
    : ICommandHandler<MarkItemReadyCommand, MarkItemReadyResult>
{
    public async Task<MarkItemReadyResult> Handle(
        MarkItemReadyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        KitchenTicket ticket = await repository.GetByIdAsync(command.TicketId, cancellationToken)
            ?? throw new KitchenTicketNotFoundException(command.TicketId);

        Instant now = SystemClock.Instance.GetCurrentInstant();
        ticket.MarkItemReady(KitchenItemId.Of(command.ItemId), now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Item {ItemId} of KitchenTicket {TicketId} is ready.",
            command.ItemId, ticket.Id.Value);

        return new MarkItemReadyResult(ticket.Id.Value, command.ItemId);
    }
}