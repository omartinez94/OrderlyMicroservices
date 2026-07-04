namespace Kitchen.API.Application.KitchenTickets.Commands;

public record AcceptOrderCommand(Guid TicketId) : ICommand<AcceptOrderResult>;

public record AcceptOrderResult(Guid TicketId);

/// <summary>
/// Accepts a <c>New</c> ticket, moving it to <c>InProgress</c> and stamping
/// the staff user as <c>ConfirmedByUserId</c>. Publishes
/// <see cref="KitchenOrderAcceptedIntegrationEvent"/> so Ordering can drive
/// <c>Order.Confirm</c> on its side.
/// </summary>
public class AcceptOrderHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IPublishEndpoint publishEndpoint,
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

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await publishEndpoint.Publish(
            new KitchenOrderAcceptedIntegrationEvent
            {
                OrderId = ticket.Id.Value,
                ConfirmedByUserId = staffUserId,
                ConfirmedAt = now,
            },
            cancellationToken);

        logger.LogInformation(
            "Accepted KitchenTicket {TicketId} for Order {OrderNumber} by user {UserId}.",
            ticket.Id.Value, ticket.OrderNumber, staffUserId);

        return new AcceptOrderResult(ticket.Id.Value);
    }
}