namespace Kitchen.API.Application.KitchenTickets.Commands;

public record BumpOrderCommand(Guid TicketId) : ICommand<BumpOrderResult>;

public record BumpOrderResult(Guid TicketId, Instant BumpedAt);

/// <summary>
/// Marks the ticket as <c>Bumped</c> after expo acknowledgment. Stages
/// <see cref="KitchenOrderBumpedIntegrationEvent"/> in the outbox for
/// downstream consumers (audit / analytics). The row is committed in the
/// same transaction as the ticket transition, so a process crash between
/// commit and broker publish can no longer lose the event.
/// If the ticket is in <c>CancelledPendingAck</c>, transitions to <c>Cancelled</c>
/// and does NOT publish <see cref="KitchenOrderBumpedIntegrationEvent"/>.
/// </summary>
public class BumpOrderHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IOutboxPublisher outboxPublisher,
    ICurrentUser currentUser,
    ILogger<BumpOrderHandler> logger)
    : ICommandHandler<BumpOrderCommand, BumpOrderResult>
{
    public async Task<BumpOrderResult> Handle(
        BumpOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid staffUserId = currentUser.UserId
            ?? throw new KitchenDomainException(
                "Authenticated staff user id is required to bump a ticket.",
                nameof(currentUser));

        KitchenTicket ticket = await repository.GetByIdAsync(command.TicketId, cancellationToken)
            ?? throw new KitchenTicketNotFoundException(command.TicketId);

        Instant now = SystemClock.Instance.GetCurrentInstant();
        KitchenTicketStatus previousStatus = ticket.Status;
        ticket.Bump(now);

        // See AcceptOrder: outbox row must commit in the same transaction
        // as the ticket transition. Publish first, then SaveChanges.
        if (previousStatus != KitchenTicketStatus.CancelledPendingAck)
        {
            await outboxPublisher.PublishAsync(
                new KitchenOrderBumpedIntegrationEvent
                {
                    OrderId = ticket.Id.Value,
                    RestaurantId = ticket.RestaurantId,
                    BumpedByUserId = staffUserId,
                    BumpedAt = now,
                },
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Bumped KitchenTicket {TicketId} (Order {OrderNumber}) by user {UserId}.",
            ticket.Id.Value, ticket.OrderNumber, staffUserId);

        return new BumpOrderResult(ticket.Id.Value, now);
    }
}
