namespace Kitchen.API.Application.KitchenTickets.Commands;

public record AcceptOrderCommand(Guid TicketId) : ICommand<AcceptOrderResult>;

public record AcceptOrderResult(Guid TicketId);

/// <summary>
/// Accepts a <c>New</c> ticket, moving it to <c>InProgress</c> and stamping
/// the staff user as <c>ConfirmedByUserId</c>. Stages
/// <see cref="KitchenOrderAcceptedIntegrationEvent"/> in the outbox so
/// Ordering can drive <c>Order.Confirm</c> on its side — the row is
/// committed in the same transaction as the ticket transition, so a
/// process crash between commit and broker publish can no longer lose the
/// event (the <c>OutboxDispatcher</c> hosted service relays the row).
/// </summary>
public class AcceptOrderHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IOutboxPublisher outboxPublisher,
    ICurrentUser currentUser,
    ILogger<AcceptOrderHandler> logger)
    : ICommandHandler<AcceptOrderCommand, AcceptOrderResult>
{
    public async Task<AcceptOrderResult> Handle(
        AcceptOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid staffUserId = currentUser.UserId
            ?? throw new KitchenDomainException(
                "Authenticated staff user id is required to accept a ticket.",
                nameof(currentUser));

        KitchenTicket ticket = await repository.GetByIdAsync(command.TicketId, cancellationToken)
            ?? throw new KitchenTicketNotFoundException(command.TicketId);

        Instant now = SystemClock.Instance.GetCurrentInstant();
        ticket.Accept(staffUserId, now);

        // Stage the outbox row BEFORE the SaveChanges so the integration
        // event commits in the same transaction as the ticket state
        // transition. A process crash between SaveChanges and the
        // background dispatcher's broker publish can no longer lose the
        // event — the row is durable the moment the ticket is.
        await outboxPublisher.PublishAsync(
            new KitchenOrderAcceptedIntegrationEvent
            {
                OrderId = ticket.Id.Value,
                ConfirmedByUserId = staffUserId,
                ConfirmedAt = now,
            },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Accepted KitchenTicket {TicketId} for Order {OrderNumber} by user {UserId}.",
            ticket.Id.Value, ticket.OrderNumber, staffUserId);

        return new AcceptOrderResult(ticket.Id.Value);
    }
}