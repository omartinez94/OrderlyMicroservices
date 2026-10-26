namespace Kitchen.API.Application.KitchenTickets.Commands;

public record RejectCancelRequestCommand(Guid TicketId, string? Reason = null) : ICommand<RejectCancelRequestResult>;

public record RejectCancelRequestResult(Guid TicketId, Instant RejectedAt);

public class RejectCancelRequestCommandValidator : AbstractValidator<RejectCancelRequestCommand>
{
    public RejectCancelRequestCommandValidator()
    {
        RuleFor(c => c.TicketId).NotEmpty();
        RuleFor(c => c.Reason).MaximumLength(500);
    }
}

/// <summary>
/// Rejects an active cancellation request on a KitchenTicket. Clears the request flag,
/// stages <see cref="KitchenOrderCancelRejectedIntegrationEvent"/> in the outbox for Ordering,
/// and broadcasts the update to KDS clients.
/// </summary>
public class RejectCancelRequestHandler(
    IKitchenTicketRepository repository,
    IUnitOfWork unitOfWork,
    IOutboxPublisher outboxPublisher,
    ICurrentUser currentUser,
    IKitchenTicketBroadcaster broadcaster,
    ILogger<RejectCancelRequestHandler> logger)
    : ICommandHandler<RejectCancelRequestCommand, RejectCancelRequestResult>
{
    public async Task<RejectCancelRequestResult> Handle(
        RejectCancelRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        KitchenTicket ticket = await repository.GetByIdAsync(command.TicketId, cancellationToken)
            ?? throw new KitchenTicketNotFoundException(command.TicketId);

        if (!ticket.IsCancelRequested)
        {
            throw new KitchenDomainException(
                $"Ticket {ticket.Id.Value} does not have an active cancellation request.",
                nameof(ticket.IsCancelRequested));
        }

        Guid? staffUserId = currentUser.UserId;
        Instant now = SystemClock.Instance.GetCurrentInstant();

        ticket.ClearCancelRequest();

        await outboxPublisher.PublishAsync(
            new KitchenOrderCancelRejectedIntegrationEvent
            {
                OrderId = ticket.Id.Value,
                RestaurantId = ticket.RestaurantId,
                OrderNumber = ticket.OrderNumber,
                RejectedAt = now,
                RejectedByUserId = staffUserId?.ToString(),
                Reason = command.Reason
            },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await broadcaster.BroadcastTicketUpdatedAsync(ticket, cancellationToken);

        logger.LogInformation(
            "Rejected cancellation request for KitchenTicket {TicketId} (Order {OrderNumber}) by user {UserId}: {Reason}.",
            ticket.Id.Value, ticket.OrderNumber, staffUserId, command.Reason);

        return new RejectCancelRequestResult(ticket.Id.Value, now);
    }
}
