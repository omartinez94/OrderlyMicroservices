namespace Ordering.Application.Orders.Commands.CancelOrder;

public class CancelOrderHandler(
    IApplicationDbContext dbContext,
    IHttpContextAccessor httpContextAccessor)
    : ICommandHandler<CancelOrderCommand, CancelOrderResult>
{
    public async Task<CancelOrderResult> Handle(
        CancelOrderCommand command,
        CancellationToken cancellationToken)
    {
        var orderId = OrderId.Of(command.OrderId);

        var order = await dbContext.Orders
            .FindAsync([orderId], cancellationToken)
            ?? throw new OrderNotFoundException(nameof(Order), command.OrderId);

        var staffUserId = ResolveStaffUserId();
        if (staffUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException(
                "Cancel requires an authenticated staff user; no subject claim was found.");
        }

        order.Cancel(command.Reason, staffUserId, SystemClock.Instance.GetCurrentInstant());

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CancelOrderResult(true);
    }

    private Guid ResolveStaffUserId()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null) return Guid.Empty;

        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}