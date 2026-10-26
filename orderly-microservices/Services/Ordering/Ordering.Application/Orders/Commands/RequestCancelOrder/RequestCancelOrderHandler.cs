namespace Ordering.Application.Orders.Commands.RequestCancelOrder;

public class RequestCancelOrderHandler(
    IApplicationDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    IOutboxPublisher outboxPublisher)
    : ICommandHandler<RequestCancelOrderCommand, RequestCancelOrderResult>
{
    public async Task<RequestCancelOrderResult> Handle(
        RequestCancelOrderCommand command,
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
                "RequestCancel requires an authenticated staff user; no subject claim was found.");
        }

        Guid staffUserId = ResolveStaffUserId(user);
        if (staffUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException(
                "RequestCancel requires an authenticated staff user; no subject claim was found.");
        }

        Instant now = SystemClock.Instance.GetCurrentInstant();
        order.RequestCancel(now, staffUserId.ToString(), command.Reason);

        OrderCancelRequestedIntegrationEvent integrationEvent = new()
        {
            OrderId = order.Id.Value,
            RestaurantId = order.RestaurantId,
            OrderNumber = order.OrderNumber.Value,
            RequestedAt = now,
            RequestedByUserId = staffUserId,
            Reason = command.Reason
        };

        await outboxPublisher.PublishAsync(integrationEvent, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RequestCancelOrderResult(true);
    }

    private static Guid ResolveStaffUserId(ClaimsPrincipal user)
    {
        string? sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out Guid id) ? id : Guid.Empty;
    }
}
