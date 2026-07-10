using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Ordering.Application.Orders.Commands.ConfirmOrder;

namespace Ordering.Application.Tests.Commands;

/// <summary>
/// Covers <see cref="ConfirmOrderHandler"/>: fetches the order, calls
/// <c>Order.Confirm</c> with the staff user id from the auth context,
/// persists. Negative paths: unknown order (404), missing auth context
/// (401 surfaced as <see cref="UnauthorizedAccessException"/>).
/// </summary>
public sealed class ConfirmOrderHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of("John Doe", "4111111111111111", "12/30", "123", "CreditCard");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order CreatePendingOrder()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task Handle_PendingOrderWithStaff_ConfirmsAndSaves()
    {
        var order = CreatePendingOrder();
        var dbContext = NewDbContextWith(order);
        var staffId = Guid.NewGuid();
        var httpContextAccessor = NewHttpContextAccessor(staffId);

        var handler = new ConfirmOrderHandler(dbContext, httpContextAccessor);
        var result = await handler.Handle(
            new ConfirmOrderCommand(order.Id.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.ConfirmedByUserId.Should().Be(staffId);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownOrder_ThrowsNotFound()
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(null));
        var httpContextAccessor = NewHttpContextAccessor(Guid.NewGuid());

        var handler = new ConfirmOrderHandler(dbContext, httpContextAccessor);

        Func<Task> act = () => handler.Handle(
            new ConfirmOrderCommand(Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<OrderNotFoundException>();
    }

    [Fact]
    public async Task Handle_MissingAuthContext_ThrowsUnauthorized()
    {
        var order = CreatePendingOrder();
        var dbContext = NewDbContextWith(order);
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var handler = new ConfirmOrderHandler(dbContext, httpContextAccessor);

        Func<Task> act = () => handler.Handle(
            new ConfirmOrderCommand(order.Id.Value),
            CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private static IApplicationDbContext NewDbContextWith(Order order)
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(order));
        return dbContext;
    }

    private static IHttpContextAccessor NewHttpContextAccessor(Guid staffUserId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, staffUserId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }
}