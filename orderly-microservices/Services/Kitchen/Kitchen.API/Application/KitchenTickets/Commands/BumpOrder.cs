namespace Kitchen.API.Application.KitchenTickets.Commands;

public record BumpOrderCommand(Guid TicketId) : ICommand<BumpOrderResult>;

public record BumpOrderResult(Guid TicketId, Instant BumpedAt);

/// <summary>
/// Marks the ticket as <c>Bumped</c> after expo acknowledgment. Stages
/// <see cref="KitchenOrderBumpedIntegrationEvent"/> in the outbox for
/// downstream consumers (audit / analytics). The row is committed in the
/// same transaction as the ticket transition, so a process crash between
/// commit and broker publish can no longer lose the event.
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
        ticket.Bump(now);

        // See AcceptOrder: outbox row must commit in the same transaction
        // as the ticket transition. Publish first, then SaveChanges.
        await outboxPublisher.PublishAsync(
            new KitchenOrderBumpedIntegrationEvent
            {
                OrderId = ticket.Id.Value,
                BumpedByUserId = staffUserId,
                BumpedAt = now,
            },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Bumped KitchenTicket {TicketId} (Order {OrderNumber}) by user {UserId}.",
            ticket.Id.Value, ticket.OrderNumber, staffUserId);

        return new BumpOrderResult(ticket.Id.Value, now);
    }
}