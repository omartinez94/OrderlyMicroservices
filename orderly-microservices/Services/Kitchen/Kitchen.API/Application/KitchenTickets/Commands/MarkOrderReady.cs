namespace Kitchen.API.Application.KitchenTickets.Commands;

public record MarkOrderReadyCommand(Guid TicketId) : ICommand<MarkOrderReadyResult>;

public record MarkOrderReadyResult(Guid TicketId, Instant ReadyAt);

/// <summary>
/// Moves a ticket from <c>InProgress</c> to <c>Ready</c>. Permitted only
/// when every item is in <c>KitchenItemStatus.Ready</c> (enforced by the
/// aggregate). Stages <see cref="KitchenOrderReadyIntegrationEvent"/> in
/// the outbox so Ordering can drive <c>Order.MarkReady</c>. The row is
/// committed in the same transaction as the ticket transition.
/// </summary>
public class MarkOrderReadyHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IOutboxPublisher outboxPublisher,
    ILogger<MarkOrderReadyHandler> logger)
    : ICommandHandler<MarkOrderReadyCommand, MarkOrderReadyResult>
{
    public async Task<MarkOrderReadyResult> Handle(
        MarkOrderReadyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        KitchenTicket ticket = await repository.GetByIdAsync(command.TicketId, cancellationToken)
            ?? throw new KitchenTicketNotFoundException(command.TicketId);

        Instant now = SystemClock.Instance.GetCurrentInstant();
        ticket.MarkReady(now);

        // See AcceptOrder: outbox row must commit in the same transaction
        // as the ticket transition. Publish first, then SaveChanges.
        await outboxPublisher.PublishAsync(
            new KitchenOrderReadyIntegrationEvent
            {
                OrderId = ticket.Id.Value,
                ReadyAt = now,
            },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "KitchenTicket {TicketId} for Order {OrderNumber} is ready.",
            ticket.Id.Value, ticket.OrderNumber);

        return new MarkOrderReadyResult(ticket.Id.Value, now);
    }
}