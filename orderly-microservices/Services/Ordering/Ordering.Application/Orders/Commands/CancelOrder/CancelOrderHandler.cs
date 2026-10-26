namespace Ordering.Application.Orders.Commands.CancelOrder;

public class CancelOrderHandler(
    IApplicationDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    IOutboxPublisher outboxPublisher)
    : ICommandHandler<CancelOrderCommand, CancelOrderResult>
{
    public async Task<CancelOrderResult> Handle(
        CancelOrderCommand command,
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
                "Cancel requires an authenticated staff user; no subject claim was found.");
        }

        Guid staffUserId = ResolveStaffUserId(user);
        if (staffUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException(
                "Cancel requires an authenticated staff user; no subject claim was found.");
        }

        bool hasApproveCancel = user.HasPermission("orders:approve_cancel");
        if (!hasApproveCancel && order.Status != OrderStatus.Pending)
        {
            throw new ForbiddenException(
                $"Cannot cancel order in status '{order.Status}' directly without 'orders:approve_cancel' permission. Cancellation must be requested via RequestCancelOrder.");
        }

        Instant now = SystemClock.Instance.GetCurrentInstant();
        order.Cancel(command.Reason, staffUserId, now);

        OrderCancelledIntegrationEvent integrationEvent = new()
        {
            OrderId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            OrderNumber = order.OrderNumber.Value,
            CancelledAt = order.CancelledAt ?? now,
            CancelledByUserId = staffUserId,
            Reason = command.Reason
        };

        await outboxPublisher.PublishAsync(integrationEvent, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CancelOrderResult(true);
    }

    private static Guid ResolveStaffUserId(ClaimsPrincipal user)
    {
        string? sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out Guid id) ? id : Guid.Empty;
    }
}
