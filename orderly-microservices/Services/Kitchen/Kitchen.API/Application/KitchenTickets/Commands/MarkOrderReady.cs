namespace Kitchen.API.Application.KitchenTickets.Commands;

public record MarkOrderReadyCommand(Guid TicketId) : ICommand<MarkOrderReadyResult>;

public record MarkOrderReadyResult(Guid TicketId, Instant ReadyAt);

/// <summary>
/// Moves a ticket from <c>InProgress</c> to <c>Ready</c>. Permitted only
/// when every item is in <c>KitchenItemStatus.Ready</c> (enforced by the
/// aggregate). Publishes <see cref="KitchenOrderReadyIntegrationEvent"/> so
/// Ordering can drive <c>Order.MarkReady</c>.
/// </summary>
public class MarkOrderReadyHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IPublishEndpoint publishEndpoint,
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

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await publishEndpoint.Publish(
            new KitchenOrderReadyIntegrationEvent
            {
                OrderId = ticket.Id.Value,
                ReadyAt = now,
            },
            cancellationToken);

        logger.LogInformation(
            "KitchenTicket {TicketId} for Order {OrderNumber} is ready.",
            ticket.Id.Value, ticket.OrderNumber);

        return new MarkOrderReadyResult(ticket.Id.Value, now);
    }
}