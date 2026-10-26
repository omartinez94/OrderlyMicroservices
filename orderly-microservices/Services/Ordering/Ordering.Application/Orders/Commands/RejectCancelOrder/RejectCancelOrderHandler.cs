namespace Ordering.Application.Orders.Commands.RejectCancelOrder;

public class RejectCancelOrderHandler(
    IApplicationDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    IOutboxPublisher outboxPublisher)
    : ICommandHandler<RejectCancelOrderCommand, RejectCancelOrderResult>
{
    public async Task<RejectCancelOrderResult> Handle(
        RejectCancelOrderCommand command,
        CancellationToken cancellationToken)
    {
        OrderId orderId = OrderId.Of(command.OrderId);

        Order order = await dbContext.Orders
            .FindAsync([orderId], cancellationToken)
            ?? throw new OrderNotFoundException(nameof(Order), command.OrderId);

        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            throw new UnauthorizedAccessException(
                "RejectCancel requires an authenticated staff user; no subject claim was found.");
        }

        Guid staffUserId = ResolveStaffUserId(user);
        if (staffUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException(
                "RejectCancel requires an authenticated staff user; no subject claim was found.");
        }

        OrderStatus restoredStatus = order.PreviousStatus ?? OrderStatus.Confirmed;
        Instant now = SystemClock.Instance.GetCurrentInstant();
        order.RejectCancel(now, staffUserId.ToString(), command.Reason);

        OrderCancelRejectedIntegrationEvent integrationEvent = new()
        {
            OrderId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            OrderNumber = order.OrderNumber.Value,
            RejectedAt = now,
            RejectedByUserId = staffUserId,
            Reason = command.Reason ?? string.Empty,
            RestoredStatus = restoredStatus.ToString()
        };

        await outboxPublisher.PublishAsync(integrationEvent, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RejectCancelOrderResult(true);
    }

    private static Guid ResolveStaffUserId(ClaimsPrincipal user)
    {
        string? sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out Guid id) ? id : Guid.Empty;
    }
}
