namespace Ordering.Application.Orders.Commands.ConfirmOrder;

public class ConfirmOrderHandler(
    IApplicationDbContext dbContext,
    IHttpContextAccessor httpContextAccessor)
    : ICommandHandler<ConfirmOrderCommand, ConfirmOrderResult>
{
    public async Task<ConfirmOrderResult> Handle(
        ConfirmOrderCommand command,
        CancellationToken cancellationToken)
    {
        OrderId? orderId = OrderId.Of(command.OrderId);

        Order? order = await dbContext.Orders
            .FindAsync([orderId], cancellationToken)
            ?? throw new OrderNotFoundException(nameof(Order), command.OrderId);

        // Staff id comes from the JWT sub claim on the kitchen display's
        // request — the endpoint is permission-gated so an empty claim
        // means the auth scheme is misconfigured and we fail closed.
        Guid staffUserId = ResolveStaffUserId();
        if (staffUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException(
                "Confirm requires an authenticated staff user; no subject claim was found.");
        }

        order.Confirm(staffUserId, SystemClock.Instance.GetCurrentInstant());

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ConfirmOrderResult(true);
    }

    private Guid ResolveStaffUserId()
    {
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return Guid.Empty;
        }

        string? sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}