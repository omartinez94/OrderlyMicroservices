namespace Kitchen.API.Application.KitchenTickets.Commands;

public record StartItemPrepCommand(Guid TicketId, Guid ItemId) : ICommand<StartItemPrepResult>;

public record StartItemPrepResult(Guid TicketId, Guid ItemId, Instant StartedAt, bool FirstItemStarted);

/// <summary>
/// Marks a single item as <c>Preparing</c>. The aggregate moves only the
/// requested item; status stays <see cref="KitchenTicketStatus.New"/> (or
/// <see cref="KitchenTicketStatus.InProgress"/>) until every item is ready.
/// On the first item-start action of a ticket — i.e. when the aggregate's
/// <c>StartedAt</c> was still <c>null</c> before this call — the handler
/// publishes <see cref="KitchenOrderPrepStartedIntegrationEvent"/> so Ordering
/// can drive <c>Order.MarkPreparing</c>. Subsequent item-starts on the same
/// ticket do not re-publish: Ordering's <c>MarkPreparing</c> is idempotent in
/// effect (it throws on a second call, which surfaces as a nack + retry and
/// is a no-op once the order is already in <c>Preparing</c>).
/// </summary>
public class StartItemPrepHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IPublishEndpoint publishEndpoint,
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

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (firstItemStarted)
        {
            await publishEndpoint.Publish(
                new KitchenOrderPrepStartedIntegrationEvent
                {
                    OrderId = ticket.Id.Value,
                    ItemId = command.ItemId,
                    StaffUserId = staffUserId,
                    StartedAt = now,
                },
                cancellationToken);
        }

        logger.LogInformation(
            "Started prep on item {ItemId} of KitchenTicket {TicketId} (firstItemStarted={First}).",
            command.ItemId, ticket.Id.Value, firstItemStarted);

        return new StartItemPrepResult(ticket.Id.Value, command.ItemId, now, firstItemStarted);
    }
}