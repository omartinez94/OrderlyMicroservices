namespace Kitchen.API.Application.KitchenTickets.Commands;

public record StartItemPrepCommand(Guid TicketId, Guid ItemId) : ICommand<StartItemPrepResult>;

public record StartItemPrepResult(Guid TicketId, Guid ItemId, Instant StartedAt, bool FirstItemStarted);

/// <summary>
/// Marks a single item as <c>Preparing</c>. The aggregate moves only the
/// requested item; status stays <see cref="KitchenTicketStatus.New"/> (or
/// <see cref="KitchenTicketStatus.InProgress"/>) until every item is ready.
/// On the first item-start action of a ticket — i.e. when the aggregate's
/// <c>StartedAt</c> was still <c>null</c> before this call — the handler
/// stages <see cref="KitchenOrderPrepStartedIntegrationEvent"/> in the
/// outbox so Ordering can drive <c>Order.MarkPreparing</c>. Subsequent
/// item-starts on the same ticket do not re-stage: Ordering's
/// <c>MarkPreparing</c> is idempotent in effect (it throws on a second
/// call, which surfaces as a nack + retry and is a no-op once the order is
/// already in <c>Preparing</c>). The outbox row is committed in the same
/// transaction as the item-prep transition.
/// </summary>
public class StartItemPrepHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IOutboxPublisher outboxPublisher,
    ICurrentUser currentUser,
    ILogger<StartItemPrepHandler> logger)
    : ICommandHandler<StartItemPrepCommand, StartItemPrepResult>
{
    public async Task<StartItemPrepResult> Handle(
        StartItemPrepCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid staffUserId = currentUser.UserId
            ?? throw new KitchenDomainException(
                "Authenticated staff user id is required to start prep on an item.",
                nameof(currentUser));

        KitchenTicket ticket = await repository.GetByIdAsync(command.TicketId, cancellationToken)
            ?? throw new KitchenTicketNotFoundException(command.TicketId);

        // Capture the "first item started" predicate BEFORE mutating the
        // aggregate: the aggregate stamps `StartedAt = now` on the first
        // call only, so a null read here means no item has begun prep yet on
        // this ticket (status is still New; Accept would have moved the
        // ticket to InProgress and stamped StartedAt too).
        bool firstItemStarted = ticket.StartedAt is null;

        Instant now = SystemClock.Instance.GetCurrentInstant();
        ticket.StartItemPrep(KitchenItemId.Of(command.ItemId), now);

        if (firstItemStarted)
        {
            await outboxPublisher.PublishAsync(
                new KitchenOrderPrepStartedIntegrationEvent
                {
                    OrderId = ticket.Id.Value,
                    ItemId = command.ItemId,
                    StaffUserId = staffUserId,
                    StartedAt = now,
                },
                cancellationToken);
        }

        // See AcceptOrder: outbox row must commit in the same transaction
        // as the item-prep transition. Publish (when applicable) BEFORE the
        // SaveChanges so the staged row is included in the same EF Core
        // transaction as the aggregate mutation.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Started prep on item {ItemId} of KitchenTicket {TicketId} (firstItemStarted={First}).",
            command.ItemId, ticket.Id.Value, firstItemStarted);

        return new StartItemPrepResult(ticket.Id.Value, command.ItemId, now, firstItemStarted);
    }
}