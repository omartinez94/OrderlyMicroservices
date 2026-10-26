namespace Kitchen.API.Application.KitchenTickets.Commands;

public record RecallOrderCommand(Guid TicketId) : ICommand<RecallOrderResult>;

public record RecallOrderResult(Guid TicketId);

/// <summary>
/// Chef pull-back: <c>Bumped</c> → <c>Ready</c>. Stages
/// <see cref="KitchenOrderRecalledIntegrationEvent"/> in the outbox so
/// Ordering reverts the order from <c>Delivered</c> back to <c>Ready</c>.
/// </summary>
public class RecallOrderHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IOutboxPublisher outboxPublisher,
    ICurrentUser currentUser,
    ILogger<RecallOrderHandler> logger)
    : ICommandHandler<RecallOrderCommand, RecallOrderResult>
{
    public async Task<RecallOrderResult> Handle(
        RecallOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid staffUserId = currentUser.UserId
            ?? throw new KitchenDomainException(
                "Authenticated staff user id is required to recall a ticket.",
                nameof(currentUser));

        KitchenTicket ticket = await repository.GetByIdAsync(command.TicketId, cancellationToken)
            ?? throw new KitchenTicketNotFoundException(command.TicketId);

        Instant now = SystemClock.Instance.GetCurrentInstant();
        ticket.Recall(now);

        await outboxPublisher.PublishAsync(
            new KitchenOrderRecalledIntegrationEvent
            {
                OrderId = ticket.Id.Value,
                KitchenTicketId = ticket.Id.Value,
                RestaurantId = ticket.RestaurantId,
                RecalledByUserId = staffUserId,
                RecalledAt = now,
            },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Recalled KitchenTicket {TicketId} (Order {OrderNumber}) by user {UserId}.",
            ticket.Id.Value, ticket.OrderNumber, staffUserId);

        return new RecallOrderResult(ticket.Id.Value);
    }
}
