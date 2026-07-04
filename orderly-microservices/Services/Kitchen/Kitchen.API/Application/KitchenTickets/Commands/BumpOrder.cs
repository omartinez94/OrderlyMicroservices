namespace Kitchen.API.Application.KitchenTickets.Commands;

public record BumpOrderCommand(Guid TicketId) : ICommand<BumpOrderResult>;

public record BumpOrderResult(Guid TicketId, Instant BumpedAt);

/// <summary>
/// Marks the ticket as <c>Bumped</c> after expo acknowledgment. Publishes
/// <see cref="KitchenOrderBumpedIntegrationEvent"/> for downstream
/// consumers (audit / analytics).
/// </summary>
public class BumpOrderHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IPublishEndpoint publishEndpoint,
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

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await publishEndpoint.Publish(
            new KitchenOrderBumpedIntegrationEvent
            {
                OrderId = ticket.Id.Value,
                BumpedByUserId = staffUserId,
                BumpedAt = now,
            },
            cancellationToken);

        logger.LogInformation(
            "Bumped KitchenTicket {TicketId} (Order {OrderNumber}) by user {UserId}.",
            ticket.Id.Value, ticket.OrderNumber, staffUserId);

        return new BumpOrderResult(ticket.Id.Value, now);
    }
}