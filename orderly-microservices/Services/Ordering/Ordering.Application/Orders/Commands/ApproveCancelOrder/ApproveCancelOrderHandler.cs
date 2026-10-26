namespace Ordering.Application.Orders.Commands.ApproveCancelOrder;

public class ApproveCancelOrderHandler(
    IApplicationDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    IOutboxPublisher outboxPublisher)
    : ICommandHandler<ApproveCancelOrderCommand, ApproveCancelOrderResult>
{
    public async Task<ApproveCancelOrderResult> Handle(
        ApproveCancelOrderCommand command,
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
                "ApproveCancel requires an authenticated staff user; no subject claim was found.");
        }

        Guid staffUserId = ResolveStaffUserId(user);
        if (staffUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException(
                "ApproveCancel requires an authenticated staff user; no subject claim was found.");
        }

        Instant now = SystemClock.Instance.GetCurrentInstant();
        order.ApproveCancel(now, staffUserId.ToString(), command.Reason);

        OrderCancelledIntegrationEvent integrationEvent = new()
        {
            OrderId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            OrderNumber = order.OrderNumber.Value,
            CancelledAt = order.CancelledAt ?? now,
            CancelledByUserId = staffUserId,
            Reason = order.CancellationReason ?? command.Reason ?? "Cancelled"
        };

        await outboxPublisher.PublishAsync(integrationEvent, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ApproveCancelOrderResult(true);
    }

    private static Guid ResolveStaffUserId(ClaimsPrincipal user)
    {
        string? sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out Guid id) ? id : Guid.Empty;
    }
}
